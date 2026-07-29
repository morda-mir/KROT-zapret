using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KROT.App.Controls
{
    public partial class KrotMascot : UserControl
    {
        public static readonly DependencyProperty IsSearchingProperty =
            DependencyProperty.Register(
                nameof(IsSearching),
                typeof(bool),
                typeof(KrotMascot),
                new PropertyMetadata(false, OnIsSearchingChanged));

        private readonly Random _random = new Random();
        private readonly DispatcherTimer _animationTimer;
        private bool _animationRunning;
        private bool _searchlightRunning;
        private bool _introPlayed;

        public KrotMascot()
        {
            InitializeComponent();

            _animationTimer = new DispatcherTimer
            {
                Interval = GetNextInterval()
            };

            _animationTimer.Tick += AnimationTimerOnTick;

            Loaded += async (_, __) =>
            {
                StartIdleAnimations();
                UpdateSearchlightAnimation();
                await PlayIntroAsync();
            };
            Unloaded += (_, __) =>
            {
                StopIdleAnimations();
                StopSearchlightAnimation();
            };
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    StartIdleAnimations();
                    UpdateSearchlightAnimation();
                }
                else
                {
                    StopIdleAnimations();
                    StopSearchlightAnimation();
                }
            };
        }

        public bool IsSearching
        {
            get => (bool)GetValue(IsSearchingProperty);
            set => SetValue(IsSearchingProperty, value);
        }

        public void StartIdleAnimations()
        {
            if (!IsVisible || _animationTimer.IsEnabled)
            {
                return;
            }

            _animationTimer.Interval = GetNextInterval();
            _animationTimer.Start();
        }

        public void StopIdleAnimations()
        {
            _animationTimer.Stop();
        }

        private TimeSpan GetNextInterval()
        {
            return TimeSpan.FromMilliseconds(_random.Next(3000, 5001));
        }

        private async System.Threading.Tasks.Task PlayIntroAsync()
        {
            if (_introPlayed)
            {
                return;
            }

            _introPlayed = true;
            await Delay(700);
            if (!IsVisible)
            {
                return;
            }

            Blink();
            await Delay(380);
            Blink();
            await Delay(520);
            NoseTwitch();
            await Delay(550);
            PawTwitch();
        }

        private async void AnimationTimerOnTick(object sender, EventArgs e)
        {
            _animationTimer.Stop();

            if (_animationRunning || !IsVisible)
            {
                ScheduleNextAnimation();
                return;
            }

            _animationRunning = true;

            try
            {
                switch (_random.Next(0, 8))
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                        Blink();
                        await Delay(330);
                        break;

                    case 4:
                        Blink();
                        await Delay(360);
                        break;

                    case 5:
                        NoseTwitch();
                        await Delay(650);
                        break;

                    case 6:
                        PawTwitch();
                        await Delay(750);
                        break;

                    default:
                        Blink();
                        await Delay(230);
                        Blink();
                        await Delay(330);
                        break;
                }
            }
            finally
            {
                _animationRunning = false;
                ScheduleNextAnimation();
            }
        }

        private void ScheduleNextAnimation()
        {
            if (!IsVisible)
            {
                return;
            }

            _animationTimer.Interval = GetNextInterval();
            _animationTimer.Start();
        }

        private void Blink()
        {
            var openEyes = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(340),
                FillBehavior = FillBehavior.Stop
            };
            openEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));
            openEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            openEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225))));
            openEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270))));

            var closedEyes = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(340),
                FillBehavior = FillBehavior.Stop
            };
            closedEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));
            closedEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            closedEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225))));
            closedEyes.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270))));

            LeftEye.BeginAnimation(OpacityProperty, openEyes);
            RightEye.BeginAnimation(OpacityProperty, openEyes);
            LeftClosedEye.BeginAnimation(OpacityProperty, closedEyes);
            RightClosedEye.BeginAnimation(OpacityProperty, closedEyes);
        }

        private void NoseTwitch()
        {
            double offset = _random.Next(0, 2) == 0 ? -4.0 : 4.0;

            var movement = new DoubleAnimation
            {
                To = offset,
                Duration = TimeSpan.FromMilliseconds(115),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            };

            NoseTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                movement);

            WhiskersTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                movement);
        }

        private void PawTwitch()
        {
            if (IsSearching)
            {
                return;
            }

            bool animateLeft = _random.Next(0, 2) == 0;
            double angle = animateLeft ? -8.0 : 8.0;

            var movement = new DoubleAnimation
            {
                To = angle,
                Duration = TimeSpan.FromMilliseconds(230),
                AutoReverse = true,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            };

            if (animateLeft)
            {
                LeftPawRotate.BeginAnimation(
                    System.Windows.Media.RotateTransform.AngleProperty,
                    movement);
            }
            else
            {
                RightPawRotate.BeginAnimation(
                    System.Windows.Media.RotateTransform.AngleProperty,
                    movement);
            }
        }

        private static System.Threading.Tasks.Task Delay(int milliseconds)
        {
            return System.Threading.Tasks.Task.Delay(milliseconds);
        }

        private static void OnIsSearchingChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            ((KrotMascot)dependencyObject).UpdateSearchlightAnimation();
        }

        private void UpdateSearchlightAnimation()
        {
            if (IsLoaded && IsVisible && IsSearching)
            {
                StartSearchlightAnimation();
            }
            else
            {
                StopSearchlightAnimation();
            }
        }

        private void StartSearchlightAnimation()
        {
            if (_searchlightRunning)
            {
                return;
            }

            _searchlightRunning = true;

            var beamSweep = new DoubleAnimation
            {
                From = -20,
                To = 20,
                Duration = TimeSpan.FromMilliseconds(1100),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            var beamPulse = new DoubleAnimation
            {
                From = 0.42,
                To = 0.68,
                Duration = TimeSpan.FromMilliseconds(720),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };

            var lampPulse = new DoubleAnimation
            {
                From = 0.38,
                To = 0.95,
                Duration = TimeSpan.FromMilliseconds(620),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            var lensPulse = new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(620),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };

            SearchlightBeamRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                beamSweep);
            SearchlightBeamGroup.BeginAnimation(OpacityProperty, beamPulse);
            LampGlow.BeginAnimation(OpacityProperty, lampPulse);
            LampLens.BeginAnimation(OpacityProperty, lensPulse);
        }

        private void StopSearchlightAnimation()
        {
            if (!_searchlightRunning)
            {
                return;
            }

            _searchlightRunning = false;
            SearchlightBeamRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                null);
            SearchlightBeamGroup.BeginAnimation(OpacityProperty, null);
            LampGlow.BeginAnimation(OpacityProperty, null);
            LampLens.BeginAnimation(OpacityProperty, null);
            SearchlightBeamRotate.Angle = 0;
            SearchlightBeamGroup.Opacity = 0;
            LampGlow.Opacity = 0.32;
            LampLens.Opacity = 0.82;
        }
    }
}
