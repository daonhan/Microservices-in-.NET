namespace ECommerce.Shared.Qa;

public static class QaPersonas
{
    public static readonly Guid CustomerHappyId = new("5ff2d67e-c6b5-4870-911f-79393ed416fd");
    public static readonly Guid CustomerDeclineId = new("be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6");
    public static readonly Guid CustomerCancelId = new("00faac97-9ae4-4b7f-b8aa-00e7c569dd66");

    public const string CustomerHappyEmail = "customer-happy@qa.test";
    public const string CustomerDeclineEmail = "customer-decline@qa.test";
    public const string CustomerCancelEmail = "customer-cancel@qa.test";
    public const string CustomerPassword = "oKNrqkO7iC#G";
    public const string CustomerRole = "Customer";

    public const int ProductHappyId = 9001;
    public const string ProductHappyName = "product-happy";
    public const decimal ProductHappyPrice = 10.00m;
    public const int ProductHappyQuantity = 2;

    public const int DefaultWarehouseId = 1;
    public const int HappyPathStockOnHand = 25;
    public const int LowStockThreshold = 0;
}
