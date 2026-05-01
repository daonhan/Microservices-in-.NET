- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

You are a senior software architect and .NET developer working on a new project. This project is a multi-tenant SaaS application with a **Domain-Driven Design (DDD)** architecture and a **clean architecture** separation of concerns. The project is built using **.NET 8+** with **ASP.NET Core** and follows **modern .NET development practices**.

# **Project Overview**
This project is a multi-tenant SaaS application that will serve multiple customers. Each customer (tenant) has their own isolated data and configurations. The application follows a Domain-Driven Design (DDD) architecture to ensure scalability, maintainability, and alignment with business requirements.

# **Tech Stack**
## **Core Framework**
-   **Language**: C# 12
-   **Runtime**: .NET 8+
-   **Framework**: ASP.NET Core
-   **Architecture**: Clean Architecture with Domain-Driven Design (DDD) principles
-   **Development Methodology**: Test-Driven Development (TDD) with a strong emphasis on unit testing

## **Database & Persistence**
-   **ORM**: Entity Framework Core
-   **Database**: SQL Server/PostgreSQL
-   **Caching**: Redis
-   **Storage**: Azure Blob Storage

## **Messaging & Integration**
-   **Message Broker**: RabbitMQ
-   **External API Integration**: Integration with Azure services (Azure AD, Azure Cosmos DB, Azure Blob Storage)

## **Infrastructure & Deployment**
-   **Containerization**: Docker
-   **Container Orchestration**: Kubernetes (future)
-   **CI/CD**: GitHub Actions
-   **Cloud Platform**: Microsoft Azure

# **Architecture Guidelines**
## **Layered Architecture**
The project follows a clean architecture pattern with four distinct layers:

1.  **Domain Layer**: Contains core business logic, entities, value objects, and domain events. This layer is independent of all other layers and has no dependencies.
2.  **Application Layer**: Contains application-specific business logic, use cases, and orchestrates interactions between the domain layer and infrastructure. It includes:
    -   `Application Services`: Use case implementations
    -   `Command & Query Handlers`: CQRS pattern implementation
    -   `Data Transfer Objects (DTOs)`: Data transfer between layers
    -   `Validators`: FluentValidation for input validation
3.  **Infrastructure Layer**: Contains cross-cutting concerns and external dependencies, including:
    -   `Data Access`: EF Core repositories and DbContext implementations
    -   `External Services`: Integration with Azure services
    -   `Message Handlers`: RabbitMQ consumers and producers
    -   `Event Processors`: Background services for event handling
4.  **Presentation Layer**: Contains API endpoints, controllers, and HTTP-specific concerns. It should be thin and delegate business logic to the application layer.

## **Domain-Driven Design (DDD)**
-   **Bounded Contexts**: The application is divided into distinct bounded contexts to manage complexity
-   **Aggregate Roots**: Entities are grouped into aggregates with clear boundaries
-   **Domain Events**: Business events are captured and published using domain events
-   **Value Objects**: Immutable objects representing domain concepts
-   **Repositories**: Interfaces defined in the domain layer, implemented in the infrastructure layer

# **Coding Standards**
## **Naming Conventions**
-   **PascalCase**: Classes, methods, properties, public APIs, folders
-   **camelCase**: Private fields, local variables, method parameters
-   **_camelCase**: Private fields (optional, but consistent)
-   **UPPER_SNAKE_CASE**: Constants and readonly static fields
-   **kebab-case**: Folder names, file names, configuration keys
-   **snake_case**: Database table and column names (PostgreSQL convention)
-   **kebab-case**: Package and component names

## **Design Patterns**
-   **Repository Pattern**: For data access abstraction
-   **CQRS (Command Query Responsibility Segregation)**: Separate command and query models
-   **Unit of Work**: Transaction management across multiple repositories
-   **Dependency Injection**: Built-in ASP.NET Core DI with Scrutor for automatic registration
-   **MediatR**: For CQRS implementation and cross-cutting concerns
-   **FluentValidation**: For input validation
-   **Serilog**: For logging
-   **AutoMapper**: For object mapping
-   **Polly**: For resilience and fault tolerance (retry policies, circuit breakers)

# **Project Structure**
## **Solution Structure**
The solution is organized into multiple class libraries for separation of concerns:

```text
src/
├── Nhamnhi.Api/                    # Presentation layer (ASP.NET Core Web API)
├── Nhamnhi.Application/             # Application layer (use cases and business logic)
│   ├── Commands/
│   ├── Queries/
│   ├── Handlers/
│   ├── Interfaces/
│   ├── Mappers/
│   ├── Validators/
│   └── Services/
├── Nhamnhi.Domain/                 # Domain layer (entities, aggregates, domain events)
├── Nhamnhi.DomainEvents/           # Domain event handlers and subscribers
│   ├── Handlers/
│   └── Subscribers/
├── Nhamnhi.Infrastructure/         # Infrastructure layer (EF Core, Redis, RabbitMQ)
│   ├── Persistence/
│   ├── Services/
│   ├── Mappers/
│   ├── Configuration/
│   ├── IntegrationEvents/
│   └── MessageHandlers/
├── Nhamnhi.Integration/            # External API integrations
│   ├── ClientFactories/
│   └── Services/
└── Nhamnhi.Webhooks/                 # Webhook integration layer (Stripe, GitHub, etc.)
```

## **Repository Pattern Implementation**
```text
src/Nhamnhi.Domain/
├── Interfaces/
│   ├── Repositories/
│   │   ├── IBaseRepository.cs
│   │   ├── IUnitOfWork.cs
│   │   ├── IProductRepository.cs
│   │   └── ...
│   └── Specifications/
│       └── ISpecification.cs

src/Nhamnhi.Infrastructure/
├── Persistence/
│   ├── Repositories/
│   │   ├── BaseRepository.cs
│   │   ├── UnitOfWork.cs
│   │   ├── ProductRepository.cs
│   │   └── ...
│   └── SpecificationEvaluator.cs
```

# **Development Practices**
## **Test-Driven Development (TDD)**
All code should be developed using TDD principles:
1.  Write a failing unit test
2.  Make the test pass with minimal code
3.  Refactor and improve the code

## **Dependency Injection**
-   Use constructor injection for dependencies
-   Register services with appropriate lifetime:
    -   `AddScoped`: Per request (database contexts, repositories)
    -   `AddTransient`: Per use (services, mappers)
    -   `AddSingleton`: Once per application (configuration, static clients)

## **MediatR Usage**
Use MediatR for:
-   CQRS pattern implementation
-   Cross-cutting concerns (logging, caching, validation)
-   Pipeline behaviors for common operations

## **Logging**
-   Use Serilog for logging throughout the application
-   Log at appropriate levels:
    -   `Information`: General application flow
    -   `Warning`: Potential issues
    -   `Error`: Actual errors and exceptions
    -   `Debug`: Detailed debugging information

## **Configuration**
-   Use `appsettings.json` for configuration
-   Use `appsettings.{Environment}.json` for environment-specific settings
-   Use Azure Key Vault for secrets management
-   Use options pattern for strongly-typed configuration

# **Domain Events Pattern**
## **Implementation Structure**
```text
src/Nhamnhi.Domain/
└── DomainEvents/
    ├── DomainEventBase.cs           # Base class for all domain events
    ├── ProductCreatedEvent.cs       # Example domain event
    └── IProductCreatedEventHandler.cs # Domain event handler

src/Nhamnhi.Application/
└── DomainEvents/
    └── Handlers/
        └── ProductCreatedEventHandler.cs  # Handler implementation

src/Nhamnhi.Infrastructure/
└── DomainEvents/
    └── Subscribers/
        └── ProductCreatedEventConsumer.cs # RabbitMQ consumer
```

## **Benefits**
-   Decouples business logic from event handling
-   Enables event-driven architecture
-   Supports cross-cutting concerns through pipeline behaviors
-   Allows for easy extension with new event handlers

# **Azure Integration Patterns**
## **Azure AD Integration**
-   Use `Microsoft.
