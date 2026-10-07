using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SuperAutoMater.Wpf.Controls
{
    public partial class BentoCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(BentoCard), new PropertyMetadata("CARD TITLE"));

        public static readonly DependencyProperty SubtitleBadgeProperty =
            DependencyProperty.Register("SubtitleBadge", typeof(string), typeof(BentoCard), new PropertyMetadata("", OnSubtitleBadgeChanged));

        public static readonly DependencyProperty StatusDotBrushProperty =
            DependencyProperty.Register("StatusDotBrush", typeof(Brush), typeof(BentoCard), new PropertyMetadata(null));

        public static readonly DependencyProperty BadgeVisibilityProperty =
            DependencyProperty.Register("BadgeVisibility", typeof(Visibility), typeof(BentoCard), new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty IsActiveCardProperty =
            DependencyProperty.Register("IsActiveCard", typeof(bool), typeof(BentoCard), new PropertyMetadata(false, OnIsActiveChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string SubtitleBadge
        {
            get => (string)GetValue(SubtitleBadgeProperty);
            set => SetValue(SubtitleBadgeProperty, value);
        }

        public Brush StatusDotBrush
        {
            get => (Brush)GetValue(StatusDotBrushProperty);
            set => SetValue(StatusDotBrushProperty, value);
        }

        public Visibility BadgeVisibility
        {
            get => (Visibility)GetValue(BadgeVisibilityProperty);
            set => SetValue(BadgeVisibilityProperty, value);
        }

        public bool IsActiveCard
        {
            get => (bool)GetValue(IsActiveCardProperty);
            set => SetValue(IsActiveCardProperty, value);
        }

        public BentoCard()
        {
            InitializeComponent();
            if (StatusDotBrush == null)
            {
                StatusDotBrush = (Brush)FindResource("BrushAccentViolet");
            }
        }

        private static void OnSubtitleBadgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BentoCard card)
            {
                card.BadgeVisibility = string.IsNullOrWhiteSpace(e.NewValue as string)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BentoCard card && card.CardBorder != null)
            {
                bool active = (bool)e.NewValue;
                card.CardBorder.Background = active
                    ? (Brush)card.FindResource("BrushActiveCardViolet")
                    : (Brush)card.FindResource("BrushCardVioletGlass");
                card.CardBorder.BorderBrush = active
                    ? (Brush)card.FindResource("BrushHairlineActive")
                    : (Brush)card.FindResource("BrushHairlineViolet");
            }
        }
    }
}
