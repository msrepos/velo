namespace Velo.Modules.Delivery.Domain;

public sealed class CrewMember
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class VanDispatch
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string VanCode { get; set; } = "";
    public string VanName { get; set; } = "";
    public Guid DriverId { get; set; }
    public Driver Driver { get; set; } = null!;
    public Guid CrewMemberId { get; set; }
    public CrewMember CrewMember { get; set; } = null!;
    public Guid StartedBy { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public Guid? EndedBy { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
