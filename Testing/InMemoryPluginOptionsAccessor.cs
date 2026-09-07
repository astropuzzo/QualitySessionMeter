using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.Testing;

/// <summary>
/// Small in-memory implementation used only by tests/simulators so the production
/// QualitySettings object can be exercised without a real N.I.N.A. profile.
/// </summary>
public sealed class InMemoryPluginOptionsAccessor : IPluginOptionsAccessor {
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    private T Get<T>(string name, T defaultValue) =>
        values.TryGetValue(name, out var value) && value is T typed ? typed : defaultValue;

    private void Set<T>(string name, T value) => values[name] = value;

    public Color GetValueColor(string name, Color defaultValue) => Get(name, defaultValue);
    public void SetValueColor(string name, Color value) => Set(name, value);
    public T GetValueEnum<T>(string name, T value) where T : struct, Enum => Get(name, value);
    public void SetValueEnum<T>(string name, T defaultValue) where T : struct, Enum => Set(name, defaultValue);
    public void SetValueBoolean(string name, bool value) => Set(name, value);
    public bool GetValueBoolean(string name, bool defaultValue) => Get(name, defaultValue);
    public void SetValueByte(string name, byte value) => Set(name, value);
    public byte GetValueByte(string name, byte defaultValue) => Get(name, defaultValue);
    public void SetValueSByte(string name, sbyte value) => Set(name, value);
    public sbyte GetValueSByte(string name, sbyte defaultValue) => Get(name, defaultValue);
    public void SetValueChar(string name, char value) => Set(name, value);
    public char GetValueChar(string name, char defaultValue) => Get(name, defaultValue);
    public void SetValueDecimal(string name, decimal value) => Set(name, value);
    public decimal GetValueDecimal(string name, decimal defaultValue) => Get(name, defaultValue);
    public void SetValueDouble(string name, double value) => Set(name, value);
    public double GetValueDouble(string name, double defaultValue) => Get(name, defaultValue);
    public void SetValueSingle(string name, float value) => Set(name, value);
    public float GetValueSingle(string name, float defaultValue) => Get(name, defaultValue);
    public void SetValueInt32(string name, int value) => Set(name, value);
    public int GetValueInt32(string name, int defaultValue) => Get(name, defaultValue);
    public void SetValueUInt32(string name, uint value) => Set(name, value);
    public uint GetValueUInt32(string name, uint defaultValue) => Get(name, defaultValue);
    public void SetValueInt64(string name, long value) => Set(name, value);
    public long GetValueInt64(string name, long defaultValue) => Get(name, defaultValue);
    public void SetValueUInt64(string name, ulong value) => Set(name, value);
    public ulong GetValueUInt64(string name, ulong defaultValue) => Get(name, defaultValue);
    public void SetValueInt16(string name, short value) => Set(name, value);
    public short GetValueInt16(string name, short defaultValue) => Get(name, defaultValue);
    public void SetValueUInt16(string name, ushort value) => Set(name, value);
    public ushort GetValueUInt16(string name, ushort defaultValue) => Get(name, defaultValue);
    public void SetValueString(string name, string value) => Set(name, value);
    public string GetValueString(string name, string defaultValue) => Get(name, defaultValue);
    public void SetValueDateTime(string name, DateTime value) => Set(name, value);
    public DateTime GetValueDateTime(string name, DateTime defaultValue) => Get(name, defaultValue);
    public void SetValueGuid(string name, Guid value) => Set(name, value);
    public Guid GetValueGuid(string name, Guid defaultValue) => Get(name, defaultValue);
}
