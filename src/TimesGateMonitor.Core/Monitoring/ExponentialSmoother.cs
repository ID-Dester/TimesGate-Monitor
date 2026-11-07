namespace TimesGateMonitor.Core.Monitoring;

public sealed class ExponentialSmoother
{
    private readonly double _newValueWeight;
    private double? _current;

    public ExponentialSmoother(double newValueWeight)
    {
        if (!double.IsFinite(newValueWeight) || newValueWeight <= 0 || newValueWeight > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(newValueWeight));
        }

        _newValueWeight = newValueWeight;
    }

    public double? Update(double? newValue)
    {
        if (!newValue.HasValue || !double.IsFinite(newValue.Value))
        {
            return null;
        }

        _current = _current.HasValue
            ? (_current.Value * (1 - _newValueWeight)) + (newValue.Value * _newValueWeight)
            : newValue.Value;

        return _current;
    }

    public void Reset() => _current = null;
}
