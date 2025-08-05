using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SchemeEditor
{
    public class ControlFactory
    {
        public Border CreateFieldContainer(int indentLevel)
        {
            return new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(indentLevel * 20, 0, 0, 5),
                Padding = new Thickness(5)
            };
        }

        public Border CreateItemContainer()
        {
            return new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(5)
            };
        }

        public Grid CreateFieldGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        public Grid CreateItemGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        public TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
        }

        public TextBox CreateTextBox(string text, Action<string> onChanged)
        {
            var textBox = new TextBox
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBox.TextChanged += (s, e) => onChanged(textBox.Text);
            return textBox;
        }

        public CheckBox CreateCheckBox(bool isChecked, Action<bool> onChanged)
        {
            var checkBox = new CheckBox
            {
                IsChecked = isChecked,
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.Checked += (s, e) => onChanged(true);
            checkBox.Unchecked += (s, e) => onChanged(false);
            return checkBox;
        }

        public ComboBox CreateEnumComboBox(Type enumType, object selectedValue, Action<object> onChanged)
        {
            var comboBox = new ComboBox
            {
                ItemsSource = Enum.GetValues(enumType),
                SelectedItem = selectedValue ?? Enum.GetValues(enumType).GetValue(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem != null)
                    onChanged(comboBox.SelectedItem);
            };
            return comboBox;
        }

        public TextBox CreateNumericTextBox(Type numericType, object value, Action<object> onChanged)
        {
            var textBox = new TextBox
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            // Format the initial display based on type
            if (numericType == typeof(uint) || numericType == typeof(UInt32))
                textBox.Text = value != null ? ((uint)value).ToString() : "0";
            else if (numericType == typeof(ulong) || numericType == typeof(UInt64))
                textBox.Text = value != null ? ((ulong)value).ToString() : "0";
            else if (numericType == typeof(ushort) || numericType == typeof(UInt16))
                textBox.Text = value != null ? ((ushort)value).ToString() : "0";
            else
                textBox.Text = value?.ToString() ?? "0";

            textBox.TextChanged += (s, e) =>
            {
                try
                {
                    object convertedValue = null;

                    if (numericType == typeof(uint) || numericType == typeof(UInt32))
                    {
                        if (uint.TryParse(textBox.Text, out uint val))
                            convertedValue = val;
                    }
                    else if (numericType == typeof(ulong) || numericType == typeof(UInt64))
                    {
                        if (ulong.TryParse(textBox.Text, out ulong val))
                            convertedValue = val;
                    }
                    else if (numericType == typeof(ushort) || numericType == typeof(UInt16))
                    {
                        if (ushort.TryParse(textBox.Text, out ushort val))
                            convertedValue = val;
                    }
                    else
                    {
                        convertedValue = Convert.ChangeType(textBox.Text, numericType);
                    }

                    if (convertedValue != null)
                        onChanged(convertedValue);
                }
                catch
                {
                }
            };
            return textBox;
        }
    }
}