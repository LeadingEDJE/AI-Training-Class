using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Request-scoped accessor for the authenticated user's identity, TPS employee id, and
/// merged privilege set (JWT claims + local <c>user_roles</c> table entries).
/// </summary>
public class CurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    IUserRoleService userRoleService) : ICurrentUserContext
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context");

    private IReadOnlyList<string>? _privileges;

    /// <inheritdoc/>
    public Guid EdjeId => Guid.Parse(
        User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim)
        ?? throw new InvalidOperationException("Missing EdjeId claim"));

    /// <inheritdoc/>
    public string Email =>
        User.FindFirstValue(System.Security.Claims.ClaimTypes.Email) ?? string.Empty;

    /// <inheritdoc/>
    // The TPS employee directory this once resolved through (Modules.Timesheet.ITpsClientService) is
    // gone with the module, and every caller identified only by EdjeId anyway once no TPS counterpart
    // existed, so EdjeId is now the sole and permanent source.
    public string TpsEmployeeId => EdjeId.ToString();

    /// <inheritdoc/>
    public IReadOnlyList<string> Privileges
    {
        get
        {
            if (_privileges is not null)
            {
                return _privileges;
            }

            // Get local roles from user_roles table
            var localRoles = userRoleService.GetRolesAsync(EdjeId).GetAwaiter().GetResult();
            var roles = new HashSet<string>(localRoles);

            // Merge ALL JWT privilege claims (IdP always wins, local is additive)
            foreach (var claim in User.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim))
            {
                roles.Add(claim.Value);
            }

            _privileges = roles.ToList().AsReadOnly();
            return _privileges;
        }
    }

    /// <inheritdoc/>
    public bool HasPrivilege(string privilege) =>
        Privileges.Contains(privilege);
}
