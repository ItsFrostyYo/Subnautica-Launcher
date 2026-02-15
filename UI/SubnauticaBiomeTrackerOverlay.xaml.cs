using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SubnauticaLauncher.Gameplay;

namespace SubnauticaLauncher.UI
{
    public partial class SubnauticaBiomeTrackerOverlay : Window
    {
        private readonly TextBlock[] _typeBlocks;
        private readonly TextBlock[] _nameBlocks;

        public SubnauticaBiomeTrackerOverlay()
        {
            InitializeComponent();
            Left = 220;
            Top = 10;

            _typeBlocks = new[] { Type1Text, Type2Text, Type3Text, Type4Text };
            _nameBlocks = new[] { Name1Text, Name2Text, Name3Text, Name4Text };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowNative.MakeClickThrough(this);
        }

        public void ApplySizePreset(Subnautica100TrackerOverlaySize size)
        {
            switch (size)
            {
                case Subnautica100TrackerOverlaySize.Small:
                    TitleText.FontSize = 12;
                    SetEntryFonts(typeSize: 7, nameSize: 8, lineHeight: 8);
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    TitleText.FontSize = 16;
                    SetEntryFonts(typeSize: 9, nameSize: 10, lineHeight: 10);
                    break;
                default:
                    TitleText.FontSize = 14;
                    SetEntryFonts(typeSize: 8, nameSize: 9, lineHeight: 9);
                    break;
            }
        }

        public void SetEntries(IReadOnlyList<(string Type, string Name)> entries)
        {
            for (int i = 0; i < _typeBlocks.Length; i++)
            {
                if (i < entries.Count)
                {
                    _typeBlocks[i].Text = entries[i].Type;
                    _nameBlocks[i].Text = entries[i].Name;
                    _typeBlocks[i].Visibility = Visibility.Visible;
                    _nameBlocks[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _typeBlocks[i].Text = string.Empty;
                    _nameBlocks[i].Text = string.Empty;
                    _typeBlocks[i].Visibility = Visibility.Collapsed;
                    _nameBlocks[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SetEntryFonts(double typeSize, double nameSize, double lineHeight)
        {
            foreach (TextBlock block in _typeBlocks)
                block.FontSize = typeSize;

            foreach (TextBlock block in _nameBlocks)
            {
                block.FontSize = nameSize;
                block.LineHeight = lineHeight;
            }
        }
    }
}
