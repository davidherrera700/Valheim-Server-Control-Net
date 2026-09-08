namespace ValheimControlApi.Data;

/// <summary>
/// A real user account, distinct from the old shared-SSH-key model. Password
/// is never stored in plain text - only a PBKDF2 hash + its salt (see
/// PasswordHasher). IsOwner always grants full access regardless of role -
/// a deliberate escape hatch so a role misconfiguration can never lock the
/// Owner out. Non-Owner users are governed entirely by their assigned
/// Role's permission set; a user with no Role assigned has none of the
/// gated permissions, only whatever's universally allowed.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public bool IsOwner { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int? RoleId { get; set; }
    public Role? Role { get; set; }
}
