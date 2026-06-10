using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// A simple accordion section implemented as a HeaderedContentControl.
    /// Unlike a UserControl, this doesn't create a naming scope boundary,
    /// so DataContext inheritance works naturally for child content.
    /// </summary>
    public class SidebarSection : HeaderedContentControl
    {
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(SidebarSection),
                new PropertyMetadata(false));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        static SidebarSection()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SidebarSection),
                new FrameworkPropertyMetadata(typeof(SidebarSection)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (GetTemplateChild("PART_Header") is Border header)
                header.MouseLeftButtonUp += (_, _) => IsExpanded = !IsExpanded;
        }
    }
}
