using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public class LicenseManager
{
    private const string MasterSalt = "MAVI_SELECT_SECRET_V1_2026_PRO_KEY";
    private const string UniversalKey = "MAVI-VICTOR-PRO-2026-FULL";
    private const string RegistrySubKey = @"Software\MaviStudio\MaviSelect\Registration";
    private const string RegistryValueName = "LicenseData";

    private readonly string _customFilePath;
    private readonly bool _useRegistry;
    private readonly Func<DateTime> _timeProvider;

    public LicenseManager(string? customFilePath = null, bool useRegistry = true, Func<DateTime>? timeProvider = null)
    {
        _customFilePath = customFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoPick",
            ".mavi_lic");
        _useRegistry = useRegistry;
        _timeProvider = timeProvider ?? (() => DateTime.UtcNow);
    }

    public static string GetMachineId()
    {
        try
        {
            string raw = string.Empty;
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (key != null)
                {
                    raw = key.GetValue("MachineGuid")?.ToString() ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = $"{Environment.MachineName}_{Environment.UserName}_{Environment.ProcessorCount}";
            }

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw + "MAVI_SELECT_HARDWARE_SALT"));
            string hex = Convert.ToHexString(hash); // 64 chars

            return $"{hex[0..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
        }
        catch
        {
            return "MAVI-DEF1-9C42-B7A0";
        }
    }

    public static string GenerateActivationKey(string machineId)
    {
        string cleanId = machineId.Trim().ToUpperInvariant();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(MasterSalt));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(cleanId));
        string hex = Convert.ToHexString(hash);

        return $"MAVI-{hex[0..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public static bool ValidateKey(string enteredKey, string machineId)
    {
        if (string.IsNullOrWhiteSpace(enteredKey)) return false;

        string normalized = enteredKey.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");

        // Universal Master Key bypass
        string normUniversal = UniversalKey.Replace("-", "");
        if (string.Equals(normalized, normUniversal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string expected = GenerateActivationKey(machineId).Replace("-", "");
        return string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase);
    }

    public LicenseState GetLicenseState()
    {
        DateTime now = _timeProvider();
        string machineId = GetMachineId();

        var stateFromDisk = LoadFromDisk();
        var stateFromReg = _useRegistry ? LoadFromRegistry() : null;

        LicenseData? currentData = null;

        if (stateFromDisk != null && stateFromReg != null)
        {
            // Cross-validation: Escolhe a data de primeiro uso mais antiga e a de último uso mais recente
            DateTime firstRun = stateFromDisk.FirstRunDateUtc < stateFromReg.FirstRunDateUtc
                ? stateFromDisk.FirstRunDateUtc
                : stateFromReg.FirstRunDateUtc;

            DateTime lastRun = stateFromDisk.LastRunDateUtc > stateFromReg.LastRunDateUtc
                ? stateFromDisk.LastRunDateUtc
                : stateFromReg.LastRunDateUtc;

            bool activated = (stateFromDisk.IsActivated && ValidateKey(stateFromDisk.ActivationKey ?? "", machineId)) ||
                             (stateFromReg.IsActivated && ValidateKey(stateFromReg.ActivationKey ?? "", machineId));

            string? key = stateFromDisk.IsActivated ? stateFromDisk.ActivationKey : stateFromReg.ActivationKey;

            currentData = new LicenseData
            {
                IsActivated = activated,
                ActivationKey = key,
                FirstRunDateUtc = firstRun,
                LastRunDateUtc = lastRun,
                MachineId = machineId
            };
        }
        else if (stateFromDisk != null)
        {
            currentData = stateFromDisk;
        }
        else if (stateFromReg != null)
        {
            currentData = stateFromReg;
        }

        // Se for o primeiro uso de todos
        if (currentData == null)
        {
            currentData = new LicenseData
            {
                IsActivated = false,
                FirstRunDateUtc = now,
                LastRunDateUtc = now,
                MachineId = machineId
            };
            SaveData(currentData);
        }

        var result = new LicenseState
        {
            MachineId = machineId,
            IsActivated = currentData.IsActivated,
            ActivationKey = currentData.ActivationKey,
            FirstRunDateUtc = currentData.FirstRunDateUtc,
            LastRunDateUtc = currentData.LastRunDateUtc
        };

        // 1. Se já está ativado com chave válida
        if (result.IsActivated && ValidateKey(result.ActivationKey ?? "", machineId))
        {
            result.DaysRemaining = 9999;
            result.IsTrialExpired = false;
            result.IsTampered = false;
            result.StatusMessage = "Mavi Select Ativado (Licença Vitalícia)";
            return result;
        }

        // 2. Detecção de Adulteração de Relógio (Clock Rollback)
        // Se a data atual for menor do que a última execução registrada (com tolerância de 5 min)
        if (now < currentData.LastRunDateUtc.AddMinutes(-5))
        {
            result.IsTampered = true;
            result.IsTrialExpired = true;
            result.DaysRemaining = 0;
            result.StatusMessage = "Data do sistema inconsistente ou relógio retrocedido.";
            return result;
        }

        // 3. Atualiza LastRunDateUtc para o momento presente
        if (now > currentData.LastRunDateUtc)
        {
            currentData.LastRunDateUtc = now;
            SaveData(currentData);
            result.LastRunDateUtc = now;
        }

        // 4. Cálculo do Período Trial (10 dias corridos)
        double elapsedDays = (now - currentData.FirstRunDateUtc).TotalDays;
        if (elapsedDays > 10.0)
        {
            result.IsTrialExpired = true;
            result.DaysRemaining = 0;
            result.StatusMessage = "Período de teste de 10 dias encerrado.";
        }
        else
        {
            result.IsTrialExpired = false;
            result.DaysRemaining = (int)Math.Max(1, Math.Ceiling(10.0 - elapsedDays));
            result.StatusMessage = $"Período de avaliação: {result.DaysRemaining} dia(s) restante(s).";
        }

        return result;
    }

    public bool Activate(string activationKey)
    {
        string machineId = GetMachineId();
        if (!ValidateKey(activationKey, machineId))
        {
            return false;
        }

        var state = GetLicenseState();
        var data = new LicenseData
        {
            IsActivated = true,
            ActivationKey = activationKey.Trim().ToUpperInvariant(),
            ActivationDateUtc = _timeProvider(),
            FirstRunDateUtc = state.FirstRunDateUtc,
            LastRunDateUtc = _timeProvider(),
            MachineId = machineId
        };

        SaveData(data);
        return true;
    }

    private void SaveData(LicenseData data)
    {
        try
        {
            byte[] rawBytes = JsonSerializer.SerializeToUtf8Bytes(data);
            byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes(data.MachineId + MasterSalt));

            byte[] protectedBytes = OperatingSystem.IsWindows()
                ? ProtectedData.Protect(rawBytes, entropy, DataProtectionScope.CurrentUser)
                : rawBytes;

            string base64 = Convert.ToBase64String(protectedBytes);

            // 1. Salva no Disco Local
            string? dir = Path.GetDirectoryName(_customFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_customFilePath, base64, Encoding.UTF8);

            // 2. Salva no Registro do Windows
            if (_useRegistry && OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
                key?.SetValue(RegistryValueName, base64, RegistryValueKind.String);
            }
        }
        catch { }
    }

    private LicenseData? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_customFilePath)) return null;
            string base64 = File.ReadAllText(_customFilePath, Encoding.UTF8);
            return DecryptData(base64);
        }
        catch
        {
            return null;
        }
    }

    private LicenseData? LoadFromRegistry()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
            if (key == null) return null;
            string? base64 = key.GetValue(RegistryValueName)?.ToString();
            if (string.IsNullOrEmpty(base64)) return null;
            return DecryptData(base64);
        }
        catch
        {
            return null;
        }
    }

    private LicenseData? DecryptData(string base64)
    {
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(base64);
            string machineId = GetMachineId();
            byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes(machineId + MasterSalt));

            byte[] rawBytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser)
                : protectedBytes;

            return JsonSerializer.Deserialize<LicenseData>(rawBytes);
        }
        catch
        {
            return null;
        }
    }

    private class LicenseData
    {
        public bool IsActivated { get; set; }
        public string? ActivationKey { get; set; }
        public DateTime? ActivationDateUtc { get; set; }
        public DateTime FirstRunDateUtc { get; set; }
        public DateTime LastRunDateUtc { get; set; }
        public string MachineId { get; set; } = string.Empty;
    }
}
