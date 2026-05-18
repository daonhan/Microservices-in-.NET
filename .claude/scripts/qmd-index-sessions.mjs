#!/usr/bin/env node
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";

const DEFAULT_COLLECTION = "nhamnhi-sessions";
const EMBED_CHUNK_BATCH_SIZE = 32;

function usage() {
    console.log(`Usage: node .claude/scripts/qmd-index-sessions.mjs [collection]

Updates and embeds only the session collection after a Claude Code SessionEnd hook.
Default collection: ${DEFAULT_COLLECTION}`);
}

function unique(values) {
    return [...new Set(values.filter(Boolean))];
}

function executableNames(command) {
    return process.platform === "win32"
        ? [`${command}.cmd`, `${command}.exe`, `${command}.ps1`, command]
        : [command];
}

function findOnPath(command) {
    const pathEntries = (process.env.PATH || "").split(path.delimiter).filter(Boolean);
    for (const entry of pathEntries) {
        for (const name of executableNames(command)) {
            const candidate = path.join(entry, name);
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }
    }

    return null;
}

function execOutput(command, args) {
    try {
        return execFileSync(command, args, {
            encoding: "utf8",
            stdio: ["ignore", "pipe", "ignore"],
        }).trim();
    } catch {
        return "";
    }
}

function packageRootCandidates() {
    const require = createRequire(import.meta.url);
    const roots = [];

    try {
        roots.push(path.resolve(path.dirname(require.resolve("@tobilu/qmd")), ".."));
    } catch {
        // The package is commonly installed globally for the qmd CLI, where bare
        // imports from this repo-local helper are not resolvable.
    }

    if (process.env.QMD_PACKAGE_ROOT) {
        roots.push(process.env.QMD_PACKAGE_ROOT);
    }

    const qmdExecutable = findOnPath("qmd");
    if (qmdExecutable) {
        const binDir = path.dirname(qmdExecutable);
        roots.push(path.join(binDir, "node_modules", "@tobilu", "qmd"));

        try {
            const realExecutable = fs.realpathSync(qmdExecutable);
            roots.push(path.resolve(path.dirname(realExecutable), ".."));
            roots.push(path.resolve(path.dirname(realExecutable), "..", ".."));
        } catch {
            // Best-effort discovery only.
        }
    }

    for (const packageManager of ["npm", "pnpm"]) {
        const globalRoot = execOutput(packageManager, ["root", "-g"]);
        if (globalRoot) {
            roots.push(path.join(globalRoot, "@tobilu", "qmd"));
        }
    }

    return unique(roots.map((root) => path.resolve(root)));
}

async function importIfExists(filePath) {
    if (!fs.existsSync(filePath)) {
        return null;
    }

    return import(pathToFileURL(filePath).href);
}

async function loadQmdModules() {
    const errors = [];

    for (const root of packageRootCandidates()) {
        const indexPath = path.join(root, "dist", "index.js");
        const storePath = path.join(root, "dist", "store.js");
        const llmPath = path.join(root, "dist", "llm.js");

        try {
            const index = await importIfExists(indexPath);
            const store = await importIfExists(storePath);
            const llm = await importIfExists(llmPath);

            if (index?.createStore && store?.insertEmbedding && llm?.withLLMSessionForLlm) {
                return { root, index, store, llm };
            }
        } catch (error) {
            errors.push(`${root}: ${error.message}`);
        }
    }

    const tried = packageRootCandidates().join(", ") || "(no candidates)";
    const details = errors.length > 0 ? ` Errors: ${errors.join("; ")}` : "";
    throw new Error(`Unable to load @tobilu/qmd SDK. Install qmd or set QMD_PACKAGE_ROOT. Tried: ${tried}.${details}`);
}

function fallbackDbPath() {
    if (process.env.INDEX_PATH) {
        return process.env.INDEX_PATH;
    }

    const homeDir = process.env.HOME || "/tmp";
    const cacheDir = process.env.XDG_CACHE_HOME || path.posix.join(homeDir.replace(/\\/g, "/"), ".cache");
    return path.resolve(cacheDir, "qmd", "index.sqlite");
}

function defaultDbPath(storeModule) {
    if (storeModule.enableProductionMode && storeModule.getDefaultDbPath) {
        storeModule.enableProductionMode();
        return storeModule.getDefaultDbPath();
    }

    return fallbackDbPath();
}

function defaultConfigPath() {
    const configDir = process.env.QMD_CONFIG_DIR
        || (process.env.XDG_CONFIG_HOME
            ? path.join(process.env.XDG_CONFIG_HOME, "qmd")
            : path.join(os.homedir(), ".config", "qmd"));

    return path.join(configDir, "index.yml");
}

function pendingDocsForCollection(db, collection) {
    return db.prepare(`
        SELECT d.hash, MIN(d.path) as path, length(CAST(c.doc AS BLOB)) as bytes
        FROM documents d
        JOIN content c ON d.hash = c.hash
        LEFT JOIN content_vectors v ON d.hash = v.hash AND v.seq = 0
        WHERE d.active = 1 AND d.collection = ? AND v.hash IS NULL
        GROUP BY d.hash
        ORDER BY MIN(d.path)
    `).all(collection);
}

function docsWithBody(db, docs) {
    if (docs.length === 0) {
        return [];
    }

    const placeholders = docs.map(() => "?").join(",");
    const rows = db.prepare(`
        SELECT hash, doc as body
        FROM content
        WHERE hash IN (${placeholders})
    `).all(...docs.map((doc) => doc.hash));
    const bodyByHash = new Map(rows.map((row) => [row.hash, row.body]));

    return docs.map((doc) => ({
        ...doc,
        body: bodyByHash.get(doc.hash) || "",
    }));
}

function buildDocBatches(docs, maxDocsPerBatch, maxBatchBytes) {
    const batches = [];
    let current = [];
    let currentBytes = 0;

    for (const doc of docs) {
        const docBytes = Math.max(0, doc.bytes || 0);
        const wouldExceedDocs = current.length >= maxDocsPerBatch;
        const wouldExceedBytes = current.length > 0 && currentBytes + docBytes > maxBatchBytes;

        if (wouldExceedDocs || wouldExceedBytes) {
            batches.push(current);
            current = [];
            currentBytes = 0;
        }

        current.push(doc);
        currentBytes += docBytes;
    }

    if (current.length > 0) {
        batches.push(current);
    }

    return batches;
}

function insertEmbeddingResult(store, storeModule, item, embedding, model, embeddedAt) {
    store.internal.ensureVecTable(embedding.embedding.length);
    storeModule.insertEmbedding(
        store.internal.db,
        item.hash,
        item.seq,
        item.pos,
        new Float32Array(embedding.embedding),
        model,
        embeddedAt,
    );
}

async function embedCollection(store, storeModule, llmModule, collection) {
    const db = store.internal.db;
    const docsToEmbed = pendingDocsForCollection(db, collection);

    if (docsToEmbed.length === 0) {
        return { docsProcessed: 0, chunksEmbedded: 0, errors: 0, durationMs: 0 };
    }

    const startedAt = Date.now();
    const model = storeModule.DEFAULT_EMBED_MODEL;
    const maxDocsPerBatch = storeModule.DEFAULT_EMBED_MAX_DOCS_PER_BATCH || 64;
    const maxBatchBytes = storeModule.DEFAULT_EMBED_MAX_BATCH_BYTES || 64 * 1024 * 1024;
    const docBatches = buildDocBatches(docsToEmbed, maxDocsPerBatch, maxBatchBytes);
    const embeddedAt = new Date().toISOString();
    const llm = store.internal.llm;
    const embedModelUri = llm.embedModelName;
    let chunksEmbedded = 0;
    let errors = 0;
    let tableInitialized = false;

    await llmModule.withLLMSessionForLlm(llm, async (session) => {
        for (const docBatch of docBatches) {
            const chunkItems = [];

            for (const doc of docsWithBody(db, docBatch)) {
                if (!doc.body.trim()) {
                    continue;
                }

                const title = storeModule.extractTitle(doc.body, doc.path);
                const chunks = await storeModule.chunkDocumentByTokens(
                    doc.body,
                    undefined,
                    undefined,
                    undefined,
                    doc.path,
                    undefined,
                    session.signal,
                );

                for (let seq = 0; seq < chunks.length; seq += 1) {
                    const chunk = chunks[seq];
                    chunkItems.push({
                        hash: doc.hash,
                        seq,
                        pos: chunk.pos,
                        text: storeModule.formatDocForEmbedding(chunk.text, title, embedModelUri),
                    });
                }
            }

            for (let offset = 0; offset < chunkItems.length; offset += EMBED_CHUNK_BATCH_SIZE) {
                const batch = chunkItems.slice(offset, offset + EMBED_CHUNK_BATCH_SIZE);

                if (!session.isValid) {
                    errors += chunkItems.length - offset;
                    break;
                }

                const remaining = [...batch];
                if (!tableInitialized) {
                    while (remaining.length > 0 && !tableInitialized) {
                        const first = remaining.shift();
                        const firstEmbedding = await session.embed(first.text, { model });

                        if (!firstEmbedding) {
                            errors += 1;
                            continue;
                        }

                        insertEmbeddingResult(store, storeModule, first, firstEmbedding, model, embeddedAt);
                        tableInitialized = true;
                        chunksEmbedded += 1;
                    }
                }

                if (remaining.length === 0) {
                    continue;
                }

                try {
                    const embeddings = await session.embedBatch(remaining.map((item) => item.text), { model });
                    for (let index = 0; index < remaining.length; index += 1) {
                        const embedding = embeddings[index];
                        if (!embedding) {
                            errors += 1;
                            continue;
                        }

                        insertEmbeddingResult(store, storeModule, remaining[index], embedding, model, embeddedAt);
                        chunksEmbedded += 1;
                    }
                } catch {
                    for (const item of remaining) {
                        try {
                            const embedding = await session.embed(item.text, { model });
                            if (!embedding) {
                                errors += 1;
                                continue;
                            }

                            insertEmbeddingResult(store, storeModule, item, embedding, model, embeddedAt);
                            chunksEmbedded += 1;
                        } catch {
                            errors += 1;
                        }
                    }
                }
            }
        }
    }, { maxDuration: 30 * 60 * 1000, name: "qmd-index-sessions" });

    return {
        docsProcessed: docsToEmbed.length,
        chunksEmbedded,
        errors,
        durationMs: Date.now() - startedAt,
    };
}

async function main() {
    if (process.argv.includes("--help") || process.argv.includes("-h")) {
        usage();
        return;
    }

    const collection = process.argv[2] || process.env.QMD_SESSIONS_COLLECTION || DEFAULT_COLLECTION;
    if (collection.startsWith("-")) {
        usage();
        throw new Error(`Unexpected option: ${collection}`);
    }

    const modules = await loadQmdModules();
    const configPath = defaultConfigPath();
    const dbPath = defaultDbPath(modules.store);
    const storeOptions = fs.existsSync(configPath)
        ? { dbPath, configPath }
        : { dbPath };
    const store = await modules.index.createStore(storeOptions);

    try {
        const collections = await store.listCollections();
        const collectionNames = collections.map((entry) => entry.name);
        if (!collectionNames.includes(collection)) {
            throw new Error(`Collection '${collection}' is not registered. Run .claude/scripts/qmd-init.sh first.`);
        }

        const update = await store.update({ collections: [collection] });
        console.log(`[qmd-session-index] updated ${collection}: collections=${update.collections} indexed=${update.indexed} updated=${update.updated} unchanged=${update.unchanged} removed=${update.removed} needsEmbedding=${update.needsEmbedding}`);

        const embed = await embedCollection(store, modules.store, modules.llm, collection);
        console.log(`[qmd-session-index] embedded ${collection}: docs=${embed.docsProcessed} chunks=${embed.chunksEmbedded} errors=${embed.errors} durationMs=${embed.durationMs}`);
    } finally {
        await store.close();
    }
}

main().catch((error) => {
    console.error(`[qmd-session-index] ERROR: ${error.message}`);
    process.exitCode = 1;
});
