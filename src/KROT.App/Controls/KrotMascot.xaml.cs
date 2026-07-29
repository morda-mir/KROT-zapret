using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KROT.App.Controls
{
    public partial class KrotMascot : UserControl
    {
        public static readonly DependencyProperty IsDiggingProperty =
            DependencyProperty.Register(
                nameof(IsDigging),
                typeof(bool),
                typeof(KrotMascot),
                new PropertyMetadata(false, OnIsDiggingChanged));

        private readonly Random _random = new Random();
        private readonly DispatcherTimer _animationTimer;
        private bool _animationRunning;
        private bool _diggingRunning;
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
                UpdateDiggingAnimation();
                await PlayIntroAsync();
            };
            Unloaded += (_, __) =>
            {
                StopIdleAnimations();
                StopDiggingAnimation();
            };
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    StartIdleAnimations();
                    UpdateDiggingAnimation();
                }
                else
                {
                    StopIdleAnimations();
                    StopDiggingAnimation();
                }
            };
        }

        public bool IsDigging
        {
            get => (bool)GetValue(IsDiggingProperty);
            set => SetValue(IsDiggingProperty, value);
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
            if (IsDigging)
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

        private static void OnIsDiggingChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            ((KrotMascot)dependencyObject).UpdateDiggingAnimation();
        }

        private void UpdateDiggingAnimation()
        {
            if (IsLoaded && IsVisible && IsDigging)
            {
                StartDiggingAnimation();
            }
            else
            {
                StopDiggingAnimation();
            }
        }

        private void StartDiggingAnimation()
        {
            if (_diggingRunning)
            {
                return;
            }

            _diggingRunning = true;
            DiggingMound.Opacity = 1;

            var moundMovement = new DoubleAnimation
            {
                From = 1,
                To = -3,
                Duration = TimeSpan.FromMilliseconds(190),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            DiggingMoundTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                moundMovement);

            StartDirtBurst(
                LeftDirtBurst,
                LeftDirtTranslate,
                horizontalOffset: -66,
                verticalOffset: -102,
                beginDelayMilliseconds: 0);
            StartDirtBurst(
                RightDirtBurst,
                RightDirtTranslate,
                horizontalOffset: 66,
                verticalOffset: -102,
                beginDelayMilliseconds: 280);
            StartDirtBurst(
                CenterDirtBurst,
                CenterDirtTranslate,
                horizontalOffset: 5,
                verticalOffset: -118,
                beginDelayMilliseconds: 140);
        }

        private static void StartDirtBurst(
            UIElement burst,
            System.Windows.Media.TranslateTransform translate,
            double horizontalOffset,
            double verticalOffset,
            int beginDelayMilliseconds)
        {
            var beginTime = TimeSpan.FromMilliseconds(beginDelayMilliseconds);
            var duration = TimeSpan.FromMilliseconds(820);
            var horizontal = new DoubleAnimation
            {
                From = 0,
                To = horizontalOffset,
                BeginTime = beginTime,
                Duration = duration,
                RepeatBehavior = RepeatBehavior.Forever,
                FillBehavior = FillBehavior.Stop
            };
            var vertical = new DoubleAnimation
            {
                From = 0,
                To = verticalOffset,
                BeginTime = beginTime,
                Duration = duration,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };
            var opacity = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = beginTime,
                Duration = duration,
                RepeatBehavior = RepeatBehavior.Forever,
                FillBehavior = FillBehavior.Stop
            };
            opacity.KeyFrames.Add(
                new DiscreteDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            opacity.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(790))));

            translate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                horizontal);
            translate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                vertical);
            burst.BeginAnimation(OpacityProperty, opacity);
        }

        private void StopDiggingAnimation()
        {
            if (!_diggingRunning)
            {
                return;
            }

            _diggingRunning = false;
            DiggingMoundTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                null);
            LeftDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                null);
            LeftDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                null);
            RightDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                null);
            RightDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                null);
            CenterDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                null);
            CenterDirtTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                null);
            LeftDirtBurst.BeginAnimation(OpacityProperty, null);
            RightDirtBurst.BeginAnimation(OpacityProperty, null);
            CenterDirtBurst.BeginAnimation(OpacityProperty, null);
            LeftDirtBurst.Opacity = 0;
            RightDirtBurst.Opacity = 0;
            CenterDirtBurst.Opacity = 0;
            DiggingMound.Opacity = 0;
            DiggingMoundTranslate.Y = 0;
        }
    }
}
