using System;
using System.IO;
using System.Text;
using System.Windows;
using GameRes;

namespace GARbro.GUI.Preview
{
    public class TextPreviewHandler : PreviewHandlerBase
    {
        private readonly MainWindow _mainWindow;
        private Stream _currentTextInput;
        
        public override bool IsActive => _mainWindow.TextView.Visibility == Visibility.Visible;

        public TextPreviewHandler(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public override void LoadContent(PreviewFile preview)
        {
            Stream file = null;
            try
            {
                var stream = VFS.OpenBinaryStream(preview.Entry);
                file = stream.AsStream;

                ScriptFormat format = ScriptFormat.FindFormat(stream);
                if (format == null)
                {
                    if (!_mainWindow.TextView.IsTextFile(file))
                    {
                        if (file.Length <= 1024 * 1024)
                        {
                            DisplayHexDump(file, preview.Entry.Name);
                        }
                        else
                        {
                            Reset();
                            _mainWindow.SetFileStatus(Localization.Format("Binary file too large for preview ({0:N0} bytes)", file.Length));
                        }
                        return;
                    }

                    var enc = _mainWindow.EncodingChoice.SelectedItem as Encoding;
                    if (null == enc)
                    {
                        enc = ScriptFormat.DetectEncoding(file, 20000);
                        _mainWindow.EncodingChoice.SelectedItem = enc;
                    }
                    _mainWindow.TextView.DisplayStream(file, enc);
                }
                else
                {
                    file.Position = 0;
                    ScriptData scriptData = format.Read(preview.Entry.Name, file);

                    _mainWindow.EncodingChoice.SelectedItem = scriptData.Encoding;

                    var displayStream = new MemoryStream();
                    scriptData.Serialize(displayStream);
                    displayStream.Position = 0;

                    _mainWindow.TextView.DisplayStream(displayStream, scriptData.Encoding);

                    string scriptInfo = string.Format("{0} - {1}",
                        format.Description,
                        scriptData.GetNewLineInfo());
                    _mainWindow.SetPreviewStatus(scriptInfo);
                }

                _mainWindow.ShowTextPreview();
                _currentTextInput = file;
                file = null;
            }
            catch (NotSupportedException)
            {
                if (file != null && file.Length <= 1024 * 1024)
                {
                    file.Position = 0;
                    DisplayHexDump(file, preview.Entry.Name);
                }
                else
                {
                    Reset();
                    _mainWindow.SetFileStatus("Binary format cannot be shown as text");
                }
            }
            catch (Exception X)
            {
                Reset();
                _mainWindow.SetFileStatus(X.Message);
            }
            finally
            {
                if (file != null)
                    file.Dispose();
            }
        }

        private void DisplayHexDump(Stream file, string filename)
        {
            try
            {
                file.Position = 0;
                var hexDump = GenerateHexDump(file);
                var hexStream = new MemoryStream(Encoding.UTF8.GetBytes(hexDump));

                _mainWindow.TextView.DisplayStream(hexStream, Encoding.UTF8);
                _mainWindow.EncodingChoice.SelectedItem = Encoding.UTF8;

                _mainWindow.ActiveViewer = _mainWindow.TextView;
                _currentTextInput = hexStream;

                _mainWindow.SetPreviewStatus(Localization.Format("Hex dump of {0} ({1:N0} bytes)",
                    System.IO.Path.GetFileName(filename), file.Length));
            }
            catch (Exception ex)
            {
                Reset();
                _mainWindow.SetFileStatus(Localization.Format("Failed to generate hex dump: {0}", ex.Message));
            }
        }

        private string GenerateHexDump(Stream stream)
        {
            var sb = new StringBuilder();
            var buffer = new byte[16];
            int offset = 0;

            sb.AppendLine(Localization._T("HexHeader0"));
            sb.AppendLine(Localization._T("HexHeader1"));

            stream.Position = 0;
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, 16)) > 0)
            {
                sb.AppendFormat("{0:X8}  ", offset);

                for (int i = 0; i < 16; i++)
                {
                    if (i < bytesRead)
                        sb.AppendFormat("{0:X2} ", buffer[i]);
                    else
                        sb.Append("   ");
                }

                sb.Append(" | ");

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b >= 0x20 && b < 0x7F)
                        sb.Append((char)b);
                    else
                        sb.Append('.');
                }

                sb.AppendLine();
                offset += bytesRead;
            }

            return sb.ToString();
        }

        public override void Reset()
        {
            _mainWindow.TextView.Clear();
            if (_currentTextInput != null)
            {
                _currentTextInput.Dispose();
                _currentTextInput = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Reset();

                base.Dispose(disposing);
            }
        }
    }
}