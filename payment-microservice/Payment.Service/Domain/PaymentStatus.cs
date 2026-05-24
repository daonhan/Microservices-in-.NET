namespace Payment.Service.Domain;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Refunded = 3,
    Failed = 4,
    Voided = 5,
}
