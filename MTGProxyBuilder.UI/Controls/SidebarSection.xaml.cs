using System.Windows;
using System.Windows.Controls;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class SidebarSection : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(SidebarSection),
                new PropertyMetadata("Section", OnHeaderChanged));

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(SidebarSection),
                new PropertyMetadata(false, OnIsExpandedChanged));

        public static readonly DependencyProperty SectionBodyProperty =
            DependencyProperty.Register(nameof(SectionBody), typeof(object), typeof(SidebarSection),
                new PropertyMetadata(null, OnSectionBodyChanged));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public object? SectionBody
        {
            get => GetValue(SectionBodyProperty);
            set => SetValue(SectionBodyProperty, value);
        }

        public SidebarSection()
        {
            InitializeComponent();
        }

        private void OnHeaderClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
                s.TitleText.Text = (string)e.NewValue;
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
            {
                bool expanded = (bool)e.NewValue;
                s.ContentBorder.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                s.ChevronText.Text = expanded ? "\u25BE" : "\u25B8";
            }
        }

        private static void OnSectionBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SidebarSection s)
                s.SectionContent.Content = e.NewValue;
        }
    }
}
