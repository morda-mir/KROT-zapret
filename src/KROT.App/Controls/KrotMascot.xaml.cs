using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KROT.App.Controls
{
    public partial class KrotMascot : UserControl
    {
        private readonly Random _random = new Random();
        private readonly DispatcherTimer _animationTimer;
        private bool _animationRunning;
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
                await PlayIntroAsync();
            };
            Unloaded += (_, __) => StopIdleAnimations();
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    StartIdleAnimations();
                }
                else
                {
                    StopIdleAnimations();
                }
            };
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
            return TimeSpan.FromSeconds(_random.Next(10, 25));
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
            LookSideways();
            await Delay(850);
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
                switch (_random.Next(0, 6))
                {
                    case 0:
                    case 1:
                        Blink();
                        await Delay(330);
                        break;

                    case 2:
                        LookSideways();
                        await Delay(900);
                        break;

                    case 3:
                        NoseTwitch();
                        await Delay(650);
                        break;

                    case 4:
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
            var blink = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(260),
                FillBehavior = FillBehavior.Stop
            };

            blink.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));

            blink.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    0.08,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));

            blink.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    1.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));

            LeftEyeScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                blink);

            RightEyeScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                blink);
        }

        private void LookSideways()
        {
            double offset = _random.Next(0, 2) == 0 ? -6.0 : 6.0;

            var movement = new DoubleAnimation
            {
                To = offset,
                Duration = TimeSpan.FromMilliseconds(260),
                AutoReverse = true,
                BeginTime = TimeSpan.FromMilliseconds(40),
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            };

            LeftEyeTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                movement);

            RightEyeTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                movement);
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
    }
}
