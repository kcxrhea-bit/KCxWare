using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KCxWare.Core.Loading;
using KCxWare.ViewModels;

namespace KCxWare.Views;

/// <summary>
/// Renders the centralized KCx loading experience: the looping neon parade (assets\final.mp4,
/// copied beside the executable at build/publish time) plus the cumulative 20/40/60/80/100
/// milestone row. Starts/stops the video based on overlay visibility so it never consumes
/// decode resources while the loading UI isn't mounted.
/// </summary>
public partial class LoadingOverlay : UserControl
{
    private static readonly Uri VideoUri = new(
        LoadingAssetLocator.ResolveVideoPath(AppContext.BaseDirectory), UriKind.Absolute);

    private bool _milestone20Active;
    private bool _milestone40Active;
    private bool _milestone60Active;
    private bool _milestone80Active;
    private bool _milestone100Active;

    public LoadingOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoadingViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is LoadingViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyMilestoneState(newVm);
            SyncVideoPlayback(newVm.IsVisible);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not LoadingViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(LoadingViewModel.IsVisible):
                SyncVideoPlayback(vm.IsVisible);
                break;
            case nameof(LoadingViewModel.Milestone20):
            case nameof(LoadingViewModel.Milestone40):
            case nameof(LoadingViewModel.Milestone60):
            case nameof(LoadingViewModel.Milestone80):
            case nameof(LoadingViewModel.Milestone100):
                ApplyMilestoneState(vm);
                break;
        }
    }

    private void SyncVideoPlayback(bool visible)
    {
        if (visible)
        {
            if (!File.Exists(VideoUri.LocalPath)) return;
            ParadeVideo.Source = VideoUri;
            ParadeVideo.Position = TimeSpan.Zero;
            ParadeVideo.Play();
            AnimateIndeterminatePulse();
        }
        else
        {
            ParadeVideo.Stop();
            ParadeVideo.Source = null;
            IndeterminatePulse.RenderTransform = Transform.Identity;
        }
    }

    private void ParadeVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        // Loop without a visible restart flash: seek to the very start and continue playback.
        ParadeVideo.Position = TimeSpan.FromMilliseconds(1);
        ParadeVideo.Play();
    }

    private void AnimateIndeterminatePulse()
    {
        if (SystemParameters.MinimizeAnimation) return; // reduced-motion: keep a static activity indicator

        var transform = new TranslateTransform();
        IndeterminatePulse.RenderTransform = transform;

        var travel = new DoubleAnimation
        {
            From = -140,
            To = 660,
            Duration = TimeSpan.FromSeconds(1.4),
            RepeatBehavior = RepeatBehavior.Forever
        };
        transform.BeginAnimation(TranslateTransform.XProperty, travel);
    }

    private void ApplyMilestoneState(LoadingViewModel vm)
    {
        UpdateMilestone(Milestone20Label, ref _milestone20Active, vm.Milestone20, MilestoneBrush.Cyan);
        UpdateMilestone(Milestone40Label, ref _milestone40Active, vm.Milestone40, MilestoneBrush.Magenta);
        UpdateMilestone(Milestone60Label, ref _milestone60Active, vm.Milestone60, MilestoneBrush.Cyan);
        UpdateMilestone(Milestone80Label, ref _milestone80Active, vm.Milestone80, MilestoneBrush.Magenta);
        UpdateMilestone(Milestone100Label, ref _milestone100Active, vm.Milestone100, MilestoneBrush.Combined);
    }

    private enum MilestoneBrush { Cyan, Magenta, Combined }

    private void UpdateMilestone(TextBlock label, ref bool wasActive, bool isActive, MilestoneBrush accent)
    {
        if (isActive == wasActive)
        {
            if (isActive) SetIlluminated(label, accent);
            return;
        }

        wasActive = isActive;
        if (isActive)
        {
            SetIlluminated(label, accent);
            BeginBloom(label);
        }
        else
        {
            SetDim(label);
        }
    }

    private static void SetDim(TextBlock label)
    {
        label.Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x4B, 0x5C));
        label.Effect = null;
    }

    private static void SetIlluminated(TextBlock label, MilestoneBrush accent)
    {
        switch (accent)
        {
            case MilestoneBrush.Cyan:
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xE6, 0xFF));
                label.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x20, 0xE6, 0xFF),
                    BlurRadius = 16,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
                break;
            case MilestoneBrush.Magenta:
                label.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x2B, 0xD6));
                label.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x2B, 0xD6),
                    BlurRadius = 16,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
                break;
            case MilestoneBrush.Combined:
                label.Foreground = new LinearGradientBrush(
                    Color.FromRgb(0x20, 0xE6, 0xFF), Color.FromRgb(0xFF, 0x2B, 0xD6), 0);
                label.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.White,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.75
                };
                break;
        }
    }

    private static void BeginBloom(TextBlock label)
    {
        if (SystemParameters.MinimizeAnimation) return; // reduced-motion proxy: skip the bloom, keep the illumination
        if (label.RenderTransform is not ScaleTransform) label.RenderTransform = new ScaleTransform(1, 1);

        var scaleUp = new DoubleAnimationUsingKeyFrames();
        scaleUp.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleUp.KeyFrames.Add(new LinearDoubleKeyFrame(1.45, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.16))));
        scaleUp.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4))));

        label.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
        label.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
    }
}
