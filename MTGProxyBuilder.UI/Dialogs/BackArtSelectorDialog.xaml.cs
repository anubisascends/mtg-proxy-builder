using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class BackArtSelectorDialog : Window
    {
        private readonly BackArtLibraryService _library;
        private readonly CardModel _card;

        /// <summary>The selected back art path, or null if cleared, or empty if cancelled.</summary>
        public string? ResultPath { get; private set; }
        public bool WasCleared { get; private set; }

        public BackArtSelectorDialog(CardModel card, BackArtLibraryService library)
        {
            InitializeComponent();
            _card = card;
            _library = library;
            CardNameLabel.Text = $"for: {card.Name}";
            BuildOptions();
        }

        private void BuildOptions()
        {
            OptionsPanel.Children.Clear();

            // Track which paths we've already shown to avoid duplicates
            var shownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Original Scryfall back (from when the card was first imported)
            if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath) && File.Exists(_card.OriginalBackArtworkPath))
            {
                bool isCurrent = string.Equals(_card.BackArtworkPath, _card.OriginalBackArtworkPath, StringComparison.OrdinalIgnoreCase);
                AddOption("Original (Scryfall)", _card.OriginalBackArtworkPath, isCurrent);
                shownPaths.Add(_card.OriginalBackArtworkPath);
            }

            // 2. Current back (if different from original and from library)
            if (!string.IsNullOrEmpty(_card.BackArtworkPath) && File.Exists(_card.BackArtworkPath)
                && !shownPaths.Contains(_card.BackArtworkPath))
            {
                AddOption("Current Back", _card.BackArtworkPath, true);
                shownPaths.Add(_card.BackArtworkPath);
            }

            // 3. Library entries
            foreach (var entry in _library.Entries)
            {
                if (File.Exists(entry.FilePath) && !shownPaths.Contains(entry.FilePath))
                {
                    AddOption(entry.Name, entry.FilePath, false);
                    shownPaths.Add(entry.FilePath);
                }
            }

            // 4. Actions
            AddActionTile("+ Add to Library", OnAddToLibrary);
            AddActionTile("Browse File...", OnBrowseFile);
        }

        private void AddOption(string label, string imagePath, bool isCurrent)
        {
            var border = new Border
            {
                Width = 100, Height = 150, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ToolTip = label
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Stretch };

            // Thumbnail
            var imgBorder = new Border
            {
                Height = 115, Background = Brushes.Black, CornerRadius = new CornerRadius(3, 3, 0, 0),
                ClipToBounds = true
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();
                imgBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                imgBorder.Child = new TextBlock
                {
                    Text = "?", Foreground = Brushes.Gray, FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            stack.Children.Add(imgBorder);

            // Label
            var lbl = new TextBlock
            {
                Text = label + (isCurrent ? " *" : ""),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 4, 4, 2)
            };
            stack.Children.Add(lbl);

            border.Child = stack;

            string path = imagePath; // capture for closure
            border.MouseLeftButtonUp += (_, _) => SelectOption(label, path, border);

            OptionsPanel.Children.Add(border);
        }

        private void AddActionTile(string label, Action action)
        {
            var border = new Border
            {
                Width = 100, Height = 150, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "+", FontSize = 28, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            border.Child = stack;
            border.MouseLeftButtonUp += (_, _) => action();

            OptionsPanel.Children.Add(border);
        }

        private void SelectOption(string label, string path, Border selectedBorder)
        {
            // Clear all borders
            foreach (var child in OptionsPanel.Children)
            {
                if (child is Border b)
                    b.BorderBrush = Brushes.Transparent;
            }
            selectedBorder.BorderBrush = Brushes.DodgerBlue;

            ResultPath = path;
            SelectedLabel.Text = label;
            SelectedPath.Text = path;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        private void OnAddToLibrary()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library"
            };
            if (dialog.ShowDialog() != true) return;

            var entry = _library.AddFromFile(dialog.FileName);
            if (entry != null)
            {
                BuildOptions(); // Rebuild to show new entry
            }
        }

        private void OnBrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Back Artwork"
            };
            if (dialog.ShowDialog() != true) return;

            ResultPath = dialog.FileName;
            SelectedLabel.Text = Path.GetFileName(dialog.FileName);
            SelectedPath.Text = dialog.FileName;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void ClearClick(object sender, RoutedEventArgs e)
        {
            WasCleared = true;
            ResultPath = null;
            DialogResult = true;
        }
    }
}
