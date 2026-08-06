using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// Per-corner tire temperatures, colored by whether each tire is cold, in its
/// optimal operating range, or too hot for the selected compound.
/// </summary>
public partial class TirePanel : PanelWindow
{
    private readonly SolidColorBrush _coldBrush;
    private readonly SolidColorBrush _okBrush;
    private readonly SolidColorBrush _hotBrush;
    private readonly SolidColorBrush _dimBrush;
    private readonly SolidColorBrush _cardBrush;
    private readonly SolidColorBrush _cardBorderBrush;
    private readonly SolidColorBrush _coldFillBrush;
    private readonly SolidColorBrush _okFillBrush;
    private readonly SolidColorBrush _hotFillBrush;

    private float _shownRangeMin = float.NaN;
    private float _shownRangeMax = float.NaN;

    public TirePanel(HudState state)
        : base(state, PanelKeys.Tires)
    {
        InitializeComponent();

        _coldBrush = (SolidColorBrush)FindResource("ColdBrush");
        _okBrush = (SolidColorBrush)FindResource("OkBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");
        _dimBrush = (SolidColorBrush)FindResource("DimBrush");
        _cardBrush = (SolidColorBrush)FindResource("CardBrush");
        _cardBorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
        _coldFillBrush = (SolidColorBrush)FindResource("TireColdFillBrush");
        _okFillBrush = (SolidColorBrush)FindResource("TireOkFillBrush");
        _hotFillBrush = (SolidColorBrush)FindResource("TireHotFillBrush");

        UpdateRangeText();
    }

    protected override void Render(Fh6Packet packet)
    {
        UpdateRangeText();
        UpdateTireTemp(TireFlCard, TireFl, TireFlState, packet.TireTempFrontLeftC);
        UpdateTireTemp(TireFrCard, TireFr, TireFrState, packet.TireTempFrontRightC);
        UpdateTireTemp(TireRlCard, TireRl, TireRlState, packet.TireTempRearLeftC);
        UpdateTireTemp(TireRrCard, TireRr, TireRrState, packet.TireTempRearRightC);
    }

    private void UpdateRangeText()
    {
        float min = State.Config.TireOptMinC;
        float max = State.Config.TireOptMaxC;
        if (min == _shownRangeMin && max == _shownRangeMax)
        {
            return;
        }

        _shownRangeMin = min;
        _shownRangeMax = max;
        TireRangeText.Text = $"TARGET {min:F0}-{max:F0} °C";
    }

    private void UpdateTireTemp(Border card, TextBlock valueBlock, TextBlock stateBlock, float tempC)
    {
        if (!float.IsFinite(tempC))
        {
            SetText(valueBlock, "--°");
            SetText(stateBlock, "--");
            valueBlock.Foreground = _dimBrush;
            stateBlock.Foreground = _dimBrush;
            card.Background = _cardBrush;
            card.BorderBrush = _cardBorderBrush;
            return;
        }

        SolidColorBrush stateBrush;
        SolidColorBrush fillBrush;
        string state;
        if (tempC < State.Config.TireOptMinC)
        {
            stateBrush = _coldBrush;
            fillBrush = _coldFillBrush;
            state = $"COLD {tempC - State.Config.TireOptMinC:+0;-0;0}°";
        }
        else if (tempC > State.Config.TireOptMaxC)
        {
            stateBrush = _hotBrush;
            fillBrush = _hotFillBrush;
            state = $"HOT {tempC - State.Config.TireOptMaxC:+0;-0;0}°";
        }
        else
        {
            stateBrush = _okBrush;
            fillBrush = _okFillBrush;
            state = "IN RANGE";
        }

        SetText(valueBlock, $"{tempC:F0}°");
        SetText(stateBlock, state);
        valueBlock.Foreground = stateBrush;
        stateBlock.Foreground = stateBrush;
        card.Background = fillBrush;
        card.BorderBrush = stateBrush;
    }
}
