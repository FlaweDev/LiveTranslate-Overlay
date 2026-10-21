/**
 * High-frequency (~120 FPS) property animation engine supporting mid-flight
 * target retargeting for Opacity and TranslateTransform coordinates.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Threading;

namespace LiveTranslateOverlay.UI.Animation;

public static class SubtitleAnimator
{
    private enum TargetProperty
    {
        Opacity,
        TranslateX,
        TranslateY
    }

    private sealed class ActiveAnimation
    {
        public required object Target { get; init; }
        public required TargetProperty Property { get; init; }
        public double From { get; set; }
        public double To { get; set; }
        public double DurationMs { get; set; }
        public long StartTimestamp { get; set; }
        public required Easing Easing { get; set; }
        public required Action<double> Setter { get; init; }
        public Action? OnCompleted { get; set; }
    }

    private static readonly List<ActiveAnimation> ActiveAnimations = new();
    private static readonly DispatcherTimer RenderTimer;
    private static readonly Easing EaseOut = new CubicEaseOut();
    private static readonly Easing EaseIn = new CubicEaseIn();

    static SubtitleAnimator()
    {
        RenderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        RenderTimer.Tick += OnRenderTick;
    }

    public static void AnimateOpacity(
        Visual target,
        double to,
        int durationMs,
        bool easeIn = false,
        Action? onCompleted = null)
    {
        StartAnimation(
            target,
            TargetProperty.Opacity,
            target.Opacity,
            to,
            durationMs,
            easeIn ? EaseIn : EaseOut,
            val => target.Opacity = val,
            onCompleted);
    }

    public static void AnimateX(
        TranslateTransform transform,
        double to,
        int durationMs,
        Action? onCompleted = null)
    {
        StartAnimation(
            transform,
            TargetProperty.TranslateX,
            transform.X,
            to,
            durationMs,
            EaseOut,
            val => transform.X = val,
            onCompleted);
    }

    public static void AnimateY(
        TranslateTransform transform,
        double to,
        int durationMs,
        Action? onCompleted = null)
    {
        StartAnimation(
            transform,
            TargetProperty.TranslateY,
            transform.Y,
            to,
            durationMs,
            EaseOut,
            val => transform.Y = val,
            onCompleted);
    }

    private static void StartAnimation(
        object target,
        TargetProperty prop,
        double from,
        double to,
        int durationMs,
        Easing easing,
        Action<double> setter,
        Action? onCompleted)
    {
        if (durationMs <= 0 || Math.Abs(from - to) < 0.001)
        {
            RemoveExisting(target, prop);
            setter(to);
            onCompleted?.Invoke();
            return;
        }

        long now = Stopwatch.GetTimestamp();

        for (int i = 0; i < ActiveAnimations.Count; i++)
        {
            var anim = ActiveAnimations[i];
            if (ReferenceEquals(anim.Target, target) && anim.Property == prop)
            {
                anim.From = from;
                anim.To = to;
                anim.DurationMs = durationMs;
                anim.StartTimestamp = now;
                anim.Easing = easing;
                anim.OnCompleted = onCompleted;
                return;
            }
        }

        ActiveAnimations.Add(new ActiveAnimation
        {
            Target = target,
            Property = prop,
            From = from,
            To = to,
            DurationMs = durationMs,
            StartTimestamp = now,
            Easing = easing,
            Setter = setter,
            OnCompleted = onCompleted
        });

        if (!RenderTimer.IsEnabled)
        {
            RenderTimer.Start();
        }
    }

    private static void RemoveExisting(object target, TargetProperty prop)
    {
        for (int i = ActiveAnimations.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(ActiveAnimations[i].Target, target) && ActiveAnimations[i].Property == prop)
            {
                ActiveAnimations.RemoveAt(i);
            }
        }
    }

    private static void OnRenderTick(object? sender, EventArgs e)
    {
        if (ActiveAnimations.Count == 0)
        {
            RenderTimer.Stop();
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;
        var callbacks = new List<Action>();

        for (int i = ActiveAnimations.Count - 1; i >= 0; i--)
        {
            var anim = ActiveAnimations[i];
            double elapsedMs = (now - anim.StartTimestamp) * 1000.0 / freq;
            double progress = Math.Clamp(elapsedMs / anim.DurationMs, 0.0, 1.0);
            double eased = anim.Easing.Ease(progress);

            anim.Setter(anim.From + (anim.To - anim.From) * eased);

            if (progress >= 1.0)
            {
                ActiveAnimations.RemoveAt(i);
                if (anim.OnCompleted != null)
                {
                    callbacks.Add(anim.OnCompleted);
                }
            }
        }

        foreach (var cb in callbacks)
        {
            cb.Invoke();
        }

        if (ActiveAnimations.Count == 0)
        {
            RenderTimer.Stop();
        }
    }
}