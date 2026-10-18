#region Using declarations
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.Gui.NinjaScript
{
    public class NRDToCSV : AddOnBase
    {
        private NTMenuItem menuItem;
        private NTMenuItem existingMenuItemInControlCenter;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NRDToCSV";
                Description = "*.nrd market replay files conversion to CSV or NPY";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null) return;

            existingMenuItemInControlCenter = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
            if (existingMenuItemInControlCenter == null) return;

            menuItem = new NTMenuItem { Header = "NRD to CSV/NPY", Style = Application.Current.TryFindResource("MainMenuItem") as Style };
            existingMenuItemInControlCenter.Items.Add(menuItem);
            menuItem.Click += OnMenuItemClick;
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (menuItem != null && window is ControlCenter)
            {
                if (existingMenuItemInControlCenter != null && existingMenuItemInControlCenter.Items.Contains(menuItem))
                    existingMenuItemInControlCenter.Items.Remove(menuItem);
                menuItem.Click -= OnMenuItemClick;
                menuItem = null;
            }
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new NRDToCSVWindow().Show()));
        }
    }

    public class NRDToCSVWindow : NTWindow, IWorkspacePersistence
    {
        private static readonly int PARALLEL_THREADS_COUNT = 4;

        private TextBox tbOutputRootDir;
        private TextBox tbSelectedInstruments;
        private ComboBox cbOutputFormat;
        private Button bConvert;
        private TextBox tbOutput;
        private Label lProgress;
        private ProgressBar pbProgress;
        private int taskCount;
        private DateTime startTimestamp;
        private long completeFilesLength;
        private long totalFilesLength;
        private bool running = false;
        private bool canceling = false;
        private bool restoringWorkspace = false;
        private string lastSelectedOutputFormat = "CSV";

        public NRDToCSVWindow()
        {
            Caption = "NRD to CSV/NPY";
            Width = 512;
            Height = 512;
            Content = BuildContent();
            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions("NRDToCSV-" + Guid.NewGuid().ToString("N"), this);
            };
            Closing += (o, e) =>
            {
                if (bConvert != null)
                    bConvert.Click -= OnConvertButtonClick;
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (running)
                canceling = true;
            base.OnClosed(e);
        }


        private DependencyObject BuildContent()
        {
            double margin = (double)FindResource("MarginBase");
            tbOutputRootDir = new TextBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                Text = Path.Combine(Globals.UserDataDir, "db", "replay.csv"),
            };
            Label lOutputRootDir = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Root directory of converted files:",
            };
            Label lOutputFormat = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Output format:",
            };
            cbOutputFormat = new ComboBox() { Margin = new Thickness(margin, 0, margin, margin) };
            cbOutputFormat.Items.Add("CSV");
            cbOutputFormat.Items.Add("NPY");
            cbOutputFormat.SelectedItem = "CSV";
            cbOutputFormat.SelectionChanged += OnOutputFormatChanged;
            tbSelectedInstruments = new TextBox() { Margin = new Thickness(margin, 0, margin, margin) };
            Label lSelectedInstruments = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, margin, margin, 0),
                Content = "Semicolon separated RegEx'es to filter *.nrd file names (keep empty to proceed all):",
            };
            bConvert = new Button() { Margin = new Thickness(margin), IsDefault = true, Content = "_Convert" };
            bConvert.Click += OnConvertButtonClick;
            tbOutput = new TextBox()
            {
                IsReadOnly = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(margin),
            };
            pbProgress = new ProgressBar()
            {
                Height = 0,
            };
            lProgress = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Height = 0,
            };

            Grid grid = new Grid() { Background = new SolidColorBrush(Colors.Transparent) };
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            Grid.SetRow(lOutputRootDir, 0);
            Grid.SetRow(tbOutputRootDir, 1);
            Grid.SetRow(lOutputFormat, 2);
            Grid.SetRow(cbOutputFormat, 3);
            Grid.SetRow(lSelectedInstruments, 4);
            Grid.SetRow(tbSelectedInstruments, 5);
            Grid.SetRow(bConvert, 6);
            Grid.SetRow(tbOutput, 7);
            Grid.SetRow(lProgress, 8);
            Grid.SetRow(pbProgress, 9);
            grid.Children.Add(lOutputRootDir);
            grid.Children.Add(tbOutputRootDir);
            grid.Children.Add(lOutputFormat);
            grid.Children.Add(cbOutputFormat);
            grid.Children.Add(lSelectedInstruments);
            grid.Children.Add(tbSelectedInstruments);
            grid.Children.Add(bConvert);
            grid.Children.Add(tbOutput);
            grid.Children.Add(lProgress);
            grid.Children.Add(pbProgress);
            return grid;
        }

        private void OnOutputFormatChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedFormat = cbOutputFormat == null ? null : cbOutputFormat.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedFormat)) return;

            if (!restoringWorkspace && tbOutputRootDir != null &&
                string.Equals(tbOutputRootDir.Text, GetDefaultOutputRoot(lastSelectedOutputFormat), StringComparison.OrdinalIgnoreCase))
                tbOutputRootDir.Text = GetDefaultOutputRoot(selectedFormat);

            lastSelectedOutputFormat = selectedFormat;
        }

        private static string GetDefaultOutputRoot(string format)
        {
            return Path.Combine(Globals.UserDataDir, "db", "replay." + format.ToLowerInvariant());
        }

        private void OnConvertButtonClick(object sender, RoutedEventArgs e)
        {
            if (tbOutput == null) return;
            logout("Run conversion...");

            if (running)
            {
                if (!canceling)
                {
                    canceling = true;
                    logout("Canceling convertion...");
                    bConvert.IsEnabled = false;
                    bConvert.Content = "Canceling...";
                }
                return;
            }

            tbOutput.Clear();

            string nrdDir = Path.Combine(Globals.UserDataDir, "db", "replay");
            string outputRootDir = tbOutputRootDir.Text;
            string outputFormat = cbOutputFormat.SelectedItem as string;
            string outputExtension = string.Equals(outputFormat, "NPY", StringComparison.OrdinalIgnoreCase) ? ".npy" : ".csv";
            List<Regex> selectedInstruments = tbSelectedInstruments.Text.IsNullOrEmpty() ? null :
                tbSelectedInstruments.Text.Split(';').Select(p => new Regex(p.Trim())).ToList();

            if (!Directory.Exists(nrdDir))
            {
                logout(string.Format("ERROR: The NRD root directory \"{0}\" not found", nrdDir));
                return;
            }

            string[] nrdSubDirs = Directory.GetDirectories(nrdDir);
            if (nrdSubDirs.Length == 0)
            {
                logout(string.Format("WARNING: The NRD root directory \"{0}\" is empty", nrdDir));
                return;
            }

            if (!Directory.Exists(outputRootDir))
            {
                try
                {
                    Directory.CreateDirectory(outputRootDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the output root directory \"{0}\": {1}", outputRootDir, error.ToString()));
                    return;
                }
            }

            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                completeFilesLength = 0;
                totalFilesLength = 0;
                List<DumpEntry> entries = new List<DumpEntry>();
                foreach (string subDir in nrdSubDirs)
                    ProceedDirectory(entries, nrdDir, subDir, outputRootDir, outputExtension, outputFormat, selectedInstruments);
                if (entries.Count == 0)
                {
                    logout("No *.nrd files found to convert");
                }
                else
                {
                    Globals.RandomDispatcher.InvokeAsync(new Action(() =>
                    {
                        logout(string.Format("Convert {0} files...", entries.Count));
                        run(entries.Count);
                        taskCount = PARALLEL_THREADS_COUNT;
                        for (int i = 0; i < taskCount; i++)
                            RunConversion(entries, i, taskCount);
                    }));
                }
            }));
        }

        private void ProceedDirectory(List<DumpEntry> entries, string nrdRoot, string nrdDir, string outputRootDir,
            string outputExtension, string outputFormat, List<Regex> selectedInstruments)
        {
            string[] fileEntries = Directory.GetFiles(nrdDir, "*.nrd");
            if (fileEntries.Length == 0)
            {
                logout(string.Format("WARNING: No *.nrd files found in \"{0}\" directory. Skipped", nrdDir));
                return;
            }

            foreach (string fileName in fileEntries)
            {
                string fullName = Path.GetFileName(Path.GetDirectoryName(fileName));
                string relativeName = fileName.Substring(nrdRoot.Length);

                if (selectedInstruments != null && selectedInstruments.Where(r => r.Match(relativeName).Success).Count() == 0)
                    continue;

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                if (instruments.Count == 0)
                {
                    logout(string.Format("Unable to find an instrument named \"{0}\". Skipped", fullName));
                    continue;
                }
                else if (instruments.Count > 1)
                {
                    logout(string.Format("More than one instrument identified for name \"{0}\". Skipped", fullName));
                    continue;
                }
                Cbi.Instrument instrument = instruments[0];
                string name = Path.GetFileNameWithoutExtension(fileName);
                string outputFileName = Path.Combine(outputRootDir, instrument.FullName, name + outputExtension);
                if (File.Exists(outputFileName))
                {
                    logout(string.Format("Conversion \"{0}\" to \"{1}\" is done already. Skipped",
                        relativeName.Substring(1), outputFileName.Substring(outputRootDir.Length + 1)));
                    continue;
                }
                long nrdFileLength = new FileInfo(fileName).Length;
                totalFilesLength += nrdFileLength;
                entries.Add(new DumpEntry()
                {
                    NrdLength = nrdFileLength,
                    Instrument = instrument,
                    Date = new DateTime(
                        Convert.ToInt16(name.Substring(0, 4)),
                        Convert.ToInt16(name.Substring(4, 2)),
                        Convert.ToInt16(name.Substring(6, 2))),
                    OutputFileName = outputFileName,
                    OutputFormat = outputFormat,
                    FromName = relativeName.Substring(1),
                    ToName = outputFileName.Substring(outputRootDir.Length + 1),
                });
            }
        }

        private void RunConversion(List<DumpEntry> entries, int offset, int increment)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                for (int i = offset; i < entries.Count; i += increment)
                {
                    ConvertNrd(entries[i]);
                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        completeFilesLength += entries[i].NrdLength;
                        string eta = "";
                        if (completeFilesLength > 0)
                        {
                            DateTime etaValue = new DateTime(
                                (long)((DateTime.Now.Ticks - startTimestamp.Ticks) * (totalFilesLength / completeFilesLength - 1)));
                            eta = string.Format(" ETA: {0}:{1}", etaValue.Day - 1, etaValue.ToString("HH:mm:ss"));
                        }
                        lProgress.Content = string.Format("{0} of {1} files converted ({2} of {3}){4}",
                            pbProgress.Value, entries.Count, ToBytes(completeFilesLength), ToBytes(totalFilesLength), eta);
                    });
                    if (canceling) break;
                }
                if (--taskCount == 0)
                {
                    complete();
                    if (canceling)
                    {
                        logout("Conversion canceled");
                    }
                    else
                    {
                        logout("Conversion complete");
                    }
                }
            }));
        }

        private void ConvertNrd(DumpEntry entry)
        {
            logout(string.Format("Conversion \"{0}\" to \"{1}\"...", entry.FromName, entry.ToName));

            string outputFileDir = Path.GetDirectoryName(entry.OutputFileName);
            string tempCsvFileName = null;
            string tempOutputFileName = null;
            if (!Directory.Exists(outputFileDir))
            {
                try
                {
                    Directory.CreateDirectory(outputFileDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the output file directory \"{0}\": {1}",
                        outputFileDir, error.ToString()));
                    return;
                }
            }

            try
            {
                if (string.Equals(entry.OutputFormat, "NPY", StringComparison.OrdinalIgnoreCase))
                {
                    string tempName = Path.GetFileNameWithoutExtension(entry.OutputFileName) + "." + Guid.NewGuid().ToString("N");
                    tempCsvFileName = Path.Combine(outputFileDir, tempName + ".csv");
                    tempOutputFileName = Path.Combine(outputFileDir, tempName + ".npy");
                    MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date.AddDays(1), entry.Date.AddDays(1), tempCsvFileName);
                    ConvertCsvToNpy(tempCsvFileName, tempOutputFileName);
                }
                else
                {
                    string tempName = Path.GetFileNameWithoutExtension(entry.OutputFileName) + "." + Guid.NewGuid().ToString("N") + ".tmp.csv";
                    tempOutputFileName = Path.Combine(outputFileDir, tempName);
                    MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date.AddDays(1), entry.Date.AddDays(1), tempOutputFileName);
                }

                File.Move(tempOutputFileName, entry.OutputFileName);
                logout(string.Format("Conversion \"{0}\" to \"{1}\" complete", entry.FromName, entry.ToName));
            }
            catch (Exception error)
            {
                logout(string.Format("ERROR: Conversion \"{0}\" to \"{1}\" failed: {2}", 
                    entry.FromName, entry.ToName, error.ToString()));
            }
            finally
            {
                DeleteTemporaryFile(tempCsvFileName);
                DeleteTemporaryFile(tempOutputFileName);
            }
        }

        private static void DeleteTemporaryFile(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
            {
                try { File.Delete(fileName); }
                catch { }
            }
        }

        private static void ConvertCsvToNpy(string csvFileName, string npyFileName)
        {
            long recordCount = 0;
            int marketMakerWidth = 1;
            using (StreamReader reader = new StreamReader(csvFileName, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    List<string> fields = SplitDelimitedLine(line);
                    ValidateRecord(fields);
                    if (fields[0].Equals("L2", StringComparison.OrdinalIgnoreCase))
                        marketMakerWidth = Math.Max(marketMakerWidth, Encoding.UTF8.GetByteCount(fields[6]));
                    recordCount++;
                }
            }

            string header = string.Format(CultureInfo.InvariantCulture,
                "{{'descr': [('level', '|u1'), ('market_data_type', '|u1'), ('timestamp', '<i8'), ('timestamp_offset', '<i4'), ('operation', '|u1'), ('position', '<i4'), ('market_maker', '|S{0}'), ('price', '<f8'), ('volume', '<i8')], 'fortran_order': False, 'shape': ({1},), }}",
                marketMakerWidth, recordCount);
            int padding = (16 - ((10 + header.Length + 1) % 16)) % 16;
            byte[] headerBytes = Encoding.ASCII.GetBytes(header + new string(' ', padding) + "\n");
            if (headerBytes.Length > ushort.MaxValue)
                throw new InvalidDataException("The NPY header is too large for format version 1.0.");

            using (FileStream output = new FileStream(npyFileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(output, Encoding.ASCII))
            {
                writer.Write((byte)0x93);
                writer.Write(Encoding.ASCII.GetBytes("NUMPY"));
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)headerBytes.Length);
                writer.Write(headerBytes);

                long recordsWritten = 0;
                byte[] marketMakerBuffer = new byte[marketMakerWidth];
                using (StreamReader reader = new StreamReader(csvFileName, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        List<string> fields = SplitDelimitedLine(line);
                        WriteNpyRecord(writer, fields, marketMakerBuffer);
                        recordsWritten++;
                    }
                }

                if (recordsWritten != recordCount)
                    throw new IOException("The CSV file changed while it was being converted.");
            }
        }

        private static List<string> SplitDelimitedLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ';' && !inQuotes)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (inQuotes) throw new FormatException("The CSV record contains an unterminated quoted field.");
            fields.Add(field.ToString());
            return fields;
        }

        private static void ValidateRecord(List<string> fields)
        {
            if (fields.Count == 6 && fields[0].Equals("L1", StringComparison.OrdinalIgnoreCase)) return;
            if (fields.Count == 9 && fields[0].Equals("L2", StringComparison.OrdinalIgnoreCase)) return;
            throw new FormatException("Unexpected market depth CSV record format.");
        }

        private static void WriteNpyRecord(BinaryWriter writer, List<string> fields, byte[] marketMakerBuffer)
        {
            ValidateRecord(fields);
            bool levelTwo = fields[0].Equals("L2", StringComparison.OrdinalIgnoreCase);
            int typeIndex = 1;
            int timestampIndex = 2;
            int offsetIndex = 3;
            int operationIndex = levelTwo ? 4 : -1;
            int positionIndex = levelTwo ? 5 : -1;
            int marketMakerIndex = levelTwo ? 6 : -1;
            int priceIndex = levelTwo ? 7 : 4;
            int volumeIndex = levelTwo ? 8 : 5;

            writer.Write((byte)(levelTwo ? 2 : 1));
            writer.Write(byte.Parse(fields[typeIndex], CultureInfo.InvariantCulture));
            writer.Write(long.Parse(fields[timestampIndex], CultureInfo.InvariantCulture));
            writer.Write(int.Parse(fields[offsetIndex], CultureInfo.InvariantCulture));
            writer.Write(levelTwo ? byte.Parse(fields[operationIndex], CultureInfo.InvariantCulture) : (byte)0);
            writer.Write(levelTwo ? int.Parse(fields[positionIndex], CultureInfo.InvariantCulture) : 0);

            Array.Clear(marketMakerBuffer, 0, marketMakerBuffer.Length);
            if (levelTwo)
            {
                byte[] marketMaker = Encoding.UTF8.GetBytes(fields[marketMakerIndex]);
                if (marketMaker.Length > marketMakerBuffer.Length)
                    throw new InvalidDataException("A market maker value is wider than the NPY field.");
                Buffer.BlockCopy(marketMaker, 0, marketMakerBuffer, 0, marketMaker.Length);
            }
            writer.Write(marketMakerBuffer);
            writer.Write(double.Parse(fields[priceIndex], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture));
            writer.Write(long.Parse(fields[volumeIndex], CultureInfo.CurrentCulture));
        }

        public void Restore(XDocument document, XElement element)
        {
            foreach (XElement elRoot in element.Elements())
            {
                if (elRoot.Name.LocalName.Contains("NRDToCSV"))
                {
                    restoringWorkspace = true;
                    XElement elOutputRootDir = elRoot.Element("OutputRootDir");
                    if (elOutputRootDir == null)
                        elOutputRootDir = elRoot.Element("CsvRootDir");
                    if (elOutputRootDir != null)
                        tbOutputRootDir.Text = elOutputRootDir.Value;

                    XElement elOutputFormat = elRoot.Element("OutputFormat");
                    if (elOutputFormat != null && (elOutputFormat.Value == "CSV" || elOutputFormat.Value == "NPY"))
                    {
                        cbOutputFormat.SelectedItem = elOutputFormat.Value;
                        lastSelectedOutputFormat = elOutputFormat.Value;
                    }

                    XElement elSelectedInstruments = elRoot.Element("SelectedInstruments");
                    if (elSelectedInstruments != null)
                        tbSelectedInstruments.Text = elSelectedInstruments.Value;
                    restoringWorkspace = false;
                }
            }
        }

        public void Save(XDocument document, XElement element)
        {
            element.Elements().Where(el => el.Name.LocalName.Equals("NRDToCSV")).Remove();
            XElement elRoot = new XElement("NRDToCSV");
            XElement elOutputRootDir = new XElement("OutputRootDir", tbOutputRootDir.Text);
            XElement elCsvRootDir = new XElement("CsvRootDir", tbOutputRootDir.Text);
            XElement elOutputFormat = new XElement("OutputFormat", cbOutputFormat.SelectedItem as string ?? "CSV");
            XElement elSelectedInstruments = new XElement("SelectedInstruments", tbSelectedInstruments.Text);
            elRoot.Add(elOutputRootDir);
            elRoot.Add(elCsvRootDir);
            elRoot.Add(elOutputFormat);
            elRoot.Add(elSelectedInstruments);
            element.Add(elRoot);
        }

        public WorkspaceOptions WorkspaceOptions { get; set; }

        private void logout(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tbOutput.AppendText(text + Environment.NewLine);
                tbOutput.ScrollToEnd();
            });
        }

        private void run(int filesCount)
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = true;
                canceling = false;
                bConvert.IsEnabled = true;
                bConvert.Content = "_Cancel";
                tbOutputRootDir.IsReadOnly = true;
                cbOutputFormat.IsEnabled = false;
                tbSelectedInstruments.IsReadOnly = true;
                double margin = (double)FindResource("MarginBase");
                lProgress.Height = 24;
                pbProgress.Margin = new Thickness(margin, 0, margin, margin);
                pbProgress.Height = 16;
                pbProgress.Minimum = 0;
                pbProgress.Maximum = filesCount;
                pbProgress.Value = 0;
                startTimestamp = DateTime.Now;
            });
        }

        private void complete()
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = false;
                lProgress.Margin = new Thickness(0);
                lProgress.Height = 0;
                pbProgress.Margin = new Thickness(0);
                pbProgress.Height = 0;
                tbOutputRootDir.IsReadOnly = false;
                cbOutputFormat.IsEnabled = true;
                tbSelectedInstruments.IsReadOnly = false;
                bConvert.IsEnabled = true;
                bConvert.Content = "_Convert";
            });
        }

        public static string ToBytes(long bytes)
        {
            if (bytes < 1024) return string.Format("{0} B", bytes);
            double exp = (int)(Math.Log(bytes) / Math.Log(1024));
            return string.Format("{0:F1} {1}iB", bytes / Math.Pow(1024, exp), "KMGTPE"[(int)exp - 1]);
        }
    }

    public class DumpEntry
    {
        public long NrdLength { get; set; }
        public Cbi.Instrument Instrument { get; set; }
        public DateTime Date { get; set; }
        public string OutputFileName { get; set; }
        public string OutputFormat { get; set; }
        public string FromName { get; set; }
        public string ToName { get; set; }
    }
}
