using System;

namespace PhotoPick.Core.Models;

public class LicenseState
{
    public bool IsActivated { get; set; }
    public string? ActivationKey { get; set; }
    public DateTime? ActivationDateUtc { get; set; }

    public DateTime FirstRunDateUtc { get; set; }
    public DateTime LastRunDateUtc { get; set; }
    public string MachineId { get; set; } = string.Empty;

    public int DaysRemaining { get; set; }
    public bool IsTrialExpired { get; set; }
    public bool IsTampered { get; set; }
    public string StatusMessage { get; set; } = string.Empty;

    public bool CanUseApp => IsActivated || (!IsTrialExpired && !IsTampered);
}
