using System.ComponentModel.DataAnnotations.Schema;

namespace ValheimControlApi.Data;

/// <summary>
/// A reusable named set of permissions, assignable to any non-Owner user.
/// Permissions are stored as a simple comma-separated string rather than a
/// separate join table - reasonable at this scale (a handful of roles, a
/// fixed small permission catalog), avoids extra schema complexity.
/// </summary>
public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [Column("Permissions")]
    public string PermissionsCsv { get; set; } = "";

    [NotMapped]
    public string[] Permissions
    {
        get => string.IsNullOrWhiteSpace(PermissionsCsv)
            ? []
            : PermissionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        set => PermissionsCsv = string.Join(',', value);
    }
}
