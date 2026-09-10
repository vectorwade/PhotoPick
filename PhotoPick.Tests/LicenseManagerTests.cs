using System;
using System.IO;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class LicenseManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _licFile;

    public LicenseManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LicTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _licFile = Path.Combine(_tempDir, ".mavi_lic");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetMachineId_ReturnsFormattedString()
    {
        string id = LicenseManager.GetMachineId();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Matches(@"^[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}$", id);
    }

    [Fact]
    public void GenerateActivationKey_And_ValidateKey_WorkCorrectly()
    {
        string machineId = "4A8F-9C12-88E1-B09A";
        string validKey = LicenseManager.GenerateActivationKey(machineId);

        Assert.StartsWith("MAVI-", validKey);
        Assert.True(LicenseManager.ValidateKey(validKey, machineId));
        Assert.True(LicenseManager.ValidateKey(validKey.ToLowerInvariant(), machineId)); // case-insensitive
        Assert.True(LicenseManager.ValidateKey(validKey.Replace("-", " "), machineId)); // spaces allowed

        Assert.False(LicenseManager.ValidateKey("MAVI-1111-2222-3333-4444", machineId));
        Assert.False(LicenseManager.ValidateKey("", machineId));

        // Master bypass key
        Assert.True(LicenseManager.ValidateKey("MAVI-VICTOR-PRO-2026-FULL", machineId));
    }

    [Fact]
    public void FirstRun_GivesFull10Days_AndCanUseApp()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lm = new LicenseManager(_licFile, useRegistry: false, () => baseTime);

        var state = lm.GetLicenseState();

        Assert.False(state.IsActivated);
        Assert.False(state.IsTrialExpired);
        Assert.False(state.IsTampered);
        Assert.True(state.CanUseApp);
        Assert.Equal(10, state.DaysRemaining);
    }

    [Fact]
    public void After5Days_Gives5DaysRemaining()
    {
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lm1 = new LicenseManager(_licFile, useRegistry: false, () => startTime);
        lm1.GetLicenseState(); // inicializa

        // Simula 5 dias depois
        var day5 = startTime.AddDays(5.0);
        var lm2 = new LicenseManager(_licFile, useRegistry: false, () => day5);
        var state2 = lm2.GetLicenseState();

        Assert.True(state2.CanUseApp);
        Assert.False(state2.IsTrialExpired);
        Assert.Equal(5, state2.DaysRemaining);
    }

    [Fact]
    public void After11Days_ExpiresTrial_AndBlocksApp()
    {
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lm1 = new LicenseManager(_licFile, useRegistry: false, () => startTime);
        lm1.GetLicenseState(); // inicializa

        // Simula 10.5 dias depois
        var expiredTime = startTime.AddDays(10.5);
        var lm2 = new LicenseManager(_licFile, useRegistry: false, () => expiredTime);
        var state = lm2.GetLicenseState();

        Assert.False(state.CanUseApp);
        Assert.True(state.IsTrialExpired);
        Assert.Equal(0, state.DaysRemaining);
    }

    [Fact]
    public void ClockRollback_DetectsTampering_AndBlocksApp()
    {
        var startTime = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var lm1 = new LicenseManager(_licFile, useRegistry: false, () => startTime);
        lm1.GetLicenseState(); // última execução gravada como 5 de janeiro

        // Usuário volta o relógio para 2 de janeiro
        var rolledBackTime = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var lm2 = new LicenseManager(_licFile, useRegistry: false, () => rolledBackTime);
        var state = lm2.GetLicenseState();

        Assert.True(state.IsTampered);
        Assert.False(state.CanUseApp);
    }

    [Fact]
    public void Activation_UnlocksAppPermanently()
    {
        var startTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var expiredTime = startTime.AddDays(15); // Já expirado
        var lm = new LicenseManager(_licFile, useRegistry: false, () => expiredTime);

        // Gera chave válida para esta máquina
        string machineId = LicenseManager.GetMachineId();
        string key = LicenseManager.GenerateActivationKey(machineId);

        bool activated = lm.Activate(key);
        Assert.True(activated);

        var state = lm.GetLicenseState();
        Assert.True(state.IsActivated);
        Assert.True(state.CanUseApp);
        Assert.False(state.IsTrialExpired);
        Assert.False(state.IsTampered);
    }
}
