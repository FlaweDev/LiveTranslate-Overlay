using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.UI.Controls;

namespace LiveTranslateOverlay.UI.Layout;

public sealed class SubtitleLayoutManager
{
    private const double BottomMargin = 30.0;
    private const double BlockSpacing = 15.0;

    private readonly Canvas _container;
    private readonly UiSettings _uiSettings;
    private readonly Dictionary<int, SubtitleBlock> _occupiedSlots = new();

    private double _containerWidth = 1920;
    private double _containerHeight = 360;
    private double _fontSize = 26;

    public SubtitleLayoutManager(Canvas container, UiSettings uiSettings)
    {
        _container = container;
        _uiSettings = uiSettings;
    }

    public void ConfigureDimensions(double width, double height, double fontSize)
    {
        _containerWidth = width;
        _containerHeight = height;
        _fontSize = fontSize;
    }

    public SubtitleBlock CreateBlock(Color origColor, Color transColor) =>
        new(origColor, transColor, _fontSize, _containerWidth, _uiSettings);

    public void AddBlock(SubtitleBlock newBlock)
    {
        if (_occupiedSlots.ContainsKey(0))
        {
            int lastOccupied = 0;
            while (_occupiedSlots.ContainsKey(lastOccupied + 1))
            {
                lastOccupied++;
            }

            for (int slot = lastOccupied; slot >= 0; slot--)
            {
                var existing = _occupiedSlots[slot];
                int nextSlot = slot + 1;

                _occupiedSlots[nextSlot] = existing;
                existing.SlotIndex = nextSlot;

                double targetY = CalculateSlotY(nextSlot, existing.BlockHeight);
                existing.AnimateToY(targetY, _uiSettings.Animation.VerticalShiftDurationMs);
            }
        }

        newBlock.SlotIndex = 0;
        _occupiedSlots[0] = newBlock;

        _container.Children.Add(newBlock);
        double slotZeroY = CalculateSlotY(0, newBlock.BlockHeight);
        newBlock.AnimateAppearance(slotZeroY, _uiSettings.Animation.SubtitleAppearDurationMs);
    }

    public void RemoveBlock(SubtitleBlock block)
    {
        if (_occupiedSlots.TryGetValue(block.SlotIndex, out var occupant) && ReferenceEquals(occupant, block))
        {
            _occupiedSlots.Remove(block.SlotIndex);
        }

        block.AnimateDisappearance(
            _uiSettings.Animation.SubtitleDisappearDurationMs,
            () => _container.Children.Remove(block));
    }

    private double CalculateSlotY(int slotIndex, double blockHeight)
    {
        double step = blockHeight + BlockSpacing;
        return _containerHeight - BottomMargin - blockHeight - (slotIndex * step);
    }
}