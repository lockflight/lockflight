namespace Orders.Contracts;

public sealed record OrderPlaced(Guid OrderId, decimal Total, string Currency);
