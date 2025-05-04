using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows.Forms;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Drawing;

using System.Linq;




namespace LedPhotoEffectAI
{

    public partial class Form1 : Form
    {
        SerialPort serialPort;
        List<LightSensorData> trainingData = new List<LightSensorData>();
        MLContext mlContext = new MLContext();
        ITransformer trainedModel;
        PredictionEngine<LightSensorData, LightSensorPrediction> predictor;
        bool isPaused = true; // Induláskor szüneteltetve legyen
        int counter = 0;
        const int maxSamples = 15; // 15 minta után automatikusan megáll
        private string[] labelNames;
        private Color currentPredictionColor = Color.Gray;//A kijelző alapértelmezettt színe
        public Form1()
        {
            InitializeComponent();
        }
        //Com port állapotjelző
        private void UpdateComPortStatus()
        {
            if (panelComStatus != null)
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    panelComStatus.BackColor = Color.Green;
                    panelComStatus.BorderStyle = BorderStyle.FixedSingle;
                }
                else
                {
                    panelComStatus.BackColor = Color.Red;
                    panelComStatus.BorderStyle = BorderStyle.FixedSingle;
                }
            }
        }
        //Betanítva, vagy még csak tanul
        private void UpdateModelStatus()
        {
            if (panelModelStatus != null)
            {
                if (trainedModel != null)
                {
                    panelModelStatus.BackColor = Color.Green;
                    panelModelStatus.BorderStyle = BorderStyle.FixedSingle;
                }
                else
                {
                    panelModelStatus.BackColor = Color.Red;
                    panelModelStatus.BorderStyle = BorderStyle.FixedSingle;
                }
            }
        }
        private void UpdateStatistics()
        {
            var countsByColor = new Dictionary<string, int>();

            foreach (var item in trainingData)
            {
                if (!countsByColor.ContainsKey(item.Color))
                    countsByColor[item.Color] = 0;

                countsByColor[item.Color]++;
            }

            string statsText = $"Összes minta: {trainingData.Count}\n";
            foreach (var kvp in countsByColor)
            {
                statsText += $"{kvp.Key}: {kvp.Value} db\n";
            }

            labelStatistics.Text = statsText;
        }
        private void UpdateColorCircle(string colorName)
        {
            colorName = colorName.ToLower();

            switch (colorName)
            {
                case "red":
                    currentPredictionColor = Color.Red;
                    break;
                case "orange":
                    currentPredictionColor = Color.Orange;
                    break;
                case "yellow":
                    currentPredictionColor = Color.Yellow;
                    break;
                case "green":
                    currentPredictionColor = Color.Green;
                    break;
                case "blue":
                    currentPredictionColor = Color.Blue;
                    break;
                case "uv":
                    currentPredictionColor = Color.Violet;
                    break;
                case "white":
                    currentPredictionColor = Color.White;
                    break;
                case "dark":
                    currentPredictionColor = Color.Black;
                    break;
                default:
                    currentPredictionColor = Color.Gray;
                    break;
            }

            panelColorCircle.Invalidate(); // frissítjük a kirajzolást
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            comboBoxComPort.Items.AddRange(SerialPort.GetPortNames());
            if (comboBoxComPort.Items.Count > 0)
                comboBoxComPort.SelectedIndex = 0;

            comboBoxColor.Items.AddRange(new string[] { "Red", "Orange", "Yellow", "Green", "Blue", "UV", "White", "Dark" });
            if (comboBoxColor.Items.Count > 0)
                comboBoxColor.SelectedIndex = 0;

            dataGridView1.AutoGenerateColumns = false;
            dataGridView1.Columns.Clear();
            dataGridView1.Columns.Add("Volt_R", "R");
            dataGridView1.Columns.Add("Volt_O", "O");
            dataGridView1.Columns.Add("Volt_Y", "Y");
            dataGridView1.Columns.Add("Volt_G", "G");
            dataGridView1.Columns.Add("Volt_B", "B");
            dataGridView1.Columns.Add("Volt_U", "U");
            UpdateComPortStatus();
            UpdateModelStatus();
            panelColorCircle.Paint += panelColorCircle_Paint;
        }
        private void buttonOpenCom_Click(object sender, EventArgs e)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen)
                    serialPort.Close();

                serialPort = new SerialPort(comboBoxComPort.SelectedItem.ToString(), 9600);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();

                if (serialPort.IsOpen)
                {
                    labelStatus.Text = $"COM port {serialPort.PortName} opened successfully.";
                }
                else
                {
                    labelStatus.Text = $"Failed to open COM port {serialPort.PortName}.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening COM port: " + ex.Message);
                labelStatus.Text = "Error opening COM port.";
            }

            if (serialPort.IsOpen)
            {
                isPaused = true;
                buttonPauseResume.Text = "Start";
                labelStatus.Text = "Adatfeldolgozás szünetel (új COM port nyitás után)";
            }
            else
            {
                labelStatus.Text = $"Failed to open COM port {serialPort.PortName}.";
            }

            UpdateComPortStatus();
        }
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (isPaused) return;

            try
            {
                string line = serialPort.ReadLine().Trim();
                string[] parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 6)
                {
                    float vR = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float vO = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float vY = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float vG = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float vB = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float vU = float.Parse(parts[5], CultureInfo.InvariantCulture);

                    Invoke((MethodInvoker)delegate
                    {
                        dataGridView1.Rows.Add(vR, vO, vY, vG, vB, vU);
                        counter++;

                        if (predictor != null)
                        {
                            var input = new LightSensorData
                            {
                                Volt_R = vR,
                                Volt_O = vO,
                                Volt_Y = vY,
                                Volt_G = vG,
                                Volt_B = vB,
                                Volt_U = vU
                            };

                            var prediction = predictor.Predict(input);

                            // ✅ Helyes confidence érték a PredictedLabelIndex alapján
                            int index = (int)prediction.PredictedLabelIndex - 1;
                            float confidence = (index >= 0 && index < prediction.Score.Length)
                                ? prediction.Score[index]
                                : 0f;

                            label1.Text = $"Predicted color: {prediction.PredictedColor} ({confidence * 100:0.0}%)";

                            // ✅ Top 3 predikció listája – helyes label sorrenddel
                            string predictionText = string.Join("\n",
                                prediction.Score
                                    .Select((prob, i) => new
                                    {
                                        Color = (i < labelNames.Length) ? labelNames[i] : $"[?{i}]",
                                        Probability = prob
                                    })
                                    .OrderByDescending(x => x.Probability)
                                    .Take(3)
                                    .Select(x => $"{x.Color}: {x.Probability * 100:0.0}%"));

                            labelPredictionDetails.Text = predictionText;
                            labelStatus.Text = $"Előrejelzés: {prediction.PredictedColor} ({counter}/{maxSamples})";

                            UpdateColorCircle(prediction.PredictedColor); // színes kör frissítés
                        }
                        else
                        {
                            labelStatus.Text = $"Minta {counter}/{maxSamples} rögzítve.";
                            labelPredictionDetails.Text = "";
                            label1.Text = "";
                        }

                        if (counter >= maxSamples)
                        {
                            isPaused = true;
                            buttonPauseResume.Text = "Start";
                            labelStatus.Text = "15 minta elkészült. Nyomd meg a Start-ot új szakasz indításához.";

                            if (serialPort != null && serialPort.IsOpen)
                            {
                                serialPort.DiscardInBuffer();
                            }
                        }
                    });
                }
                else
                {
                    Invoke((MethodInvoker)(() => labelStatus.Text = "Hibás adat: nem 6 érték!"));
                }
            }
            catch
            {
                Invoke((MethodInvoker)(() => labelStatus.Text = "Hibás adat érkezett."));
            }
        }





        private void buttonSave_Click(object sender, EventArgs e)
        {

            if (dataGridView1.Rows.Count > 1) // Minimum 1 adat, plusz 1 új üres sor
            {
                using (var writer = new StreamWriter("training_data.csv", append: true))
                {
                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        if (row.IsNewRow) continue; // Üres új sor kihagyása

                        var data = new LightSensorData
                        {
                            Volt_R = Convert.ToSingle(row.Cells[0].Value),
                            Volt_O = Convert.ToSingle(row.Cells[1].Value),
                            Volt_Y = Convert.ToSingle(row.Cells[2].Value),
                            Volt_G = Convert.ToSingle(row.Cells[3].Value),
                            Volt_B = Convert.ToSingle(row.Cells[4].Value),
                            Volt_U = Convert.ToSingle(row.Cells[5].Value),
                            Color = comboBoxColor.SelectedItem.ToString()
                        };

                        trainingData.Add(data);

                        string line = string.Join("\t", new string[]
                       {
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            data.Volt_R.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Volt_O.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Volt_Y.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Volt_G.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Volt_B.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Volt_U.ToString("0.000", new CultureInfo("hu-HU")),
                            data.Color
                       });


                        // Írás a fájlba egy sorban tabulátorral elválasztva pl. 2,11
                        writer.WriteLine(line);
                    }
                }
                labelStatus.Text = $"Elmentve {dataGridView1.Rows.Count - 1} minta mint '{comboBoxColor.SelectedItem}'. Összesen: {trainingData.Count} minta.";
                dataGridView1.Rows.Clear();
                UpdateStatistics(); // statisztikát is frissítjük
            }
            else
            {
                MessageBox.Show("Nincs elmenthető adat! Legalább 1 teljes sor szükséges.");
            }
        }
        private void buttonTrain_Click(object sender, EventArgs e)
        {
            if (trainingData.Count < 5)
            {
                MessageBox.Show("Túl kevés adat! Gyűjts több mintát.");
                return;
            }

            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(LightSensorData.Color))
                .Append(mlContext.Transforms.Concatenate("Features",
                    nameof(LightSensorData.Volt_R), nameof(LightSensorData.Volt_O),
                    nameof(LightSensorData.Volt_Y), nameof(LightSensorData.Volt_G),
                    nameof(LightSensorData.Volt_B), nameof(LightSensorData.Volt_U)))
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy())
                .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                    outputColumnName: nameof(LightSensorPrediction.PredictedColor),
                    inputColumnName: "PredictedLabel"));

            trainedModel = pipeline.Fit(dataView);
            predictor = mlContext.Model.CreatePredictionEngine<LightSensorData, LightSensorPrediction>(trainedModel);

            labelStatus.Text = "Model trained!";
            UpdateModelStatus();

            // 🔁 Helyes sorrendű labelNames generálása (betanítás szerint)
            var labelBuffer = mlContext.Data.CreateEnumerable<LightSensorData>(dataView, reuseRowObject: false)
                .Select(x => x.Color)
                .ToList();

            labelNames = labelBuffer
                .GroupBy(x => x)
                .Select(g => g.Key)
                .ToArray();
        }


        private string FormatPredictionConfidence(float[] scores, string[] labels)
        {
            if (scores == null || labels == null || scores.Length != labels.Length)
                return "Érvénytelen predikció.";

            var ranked = scores
                .Select((score, index) => new { Label = labels[index], Score = score })
                .OrderByDescending(x => x.Score)
                .ToArray();

            var best = ranked[0];
            var second = ranked.Length > 1 ? ranked[1] : null;

            // 1. Ha az első nagyon dominál
            if (best.Score >= 0.95)
                return $"✓ Magabiztos döntés: {best.Label} ({best.Score * 100:0.0}%)";

            // 2. Ha az első kettő közel van egymáshoz
            if (second != null && best.Score - second.Score < 0.15)
                return $"⚠ Közeli verseny:\n{best.Label}: {best.Score * 100:0.0}%\n{second.Label}: {second.Score * 100:0.0}%";

            // 3. Általános eset: mutassuk a top 3-at
            var top3 = ranked.Take(3)
                .Select(x => $"{x.Label}: {x.Score * 100:0.0}%");

            return "🔍 Valószínűségek:\n" + string.Join("\n", top3);
        }
        private void buttonPauseResume_Click(object sender, EventArgs e)
        {
            isPaused = !isPaused;
            buttonPauseResume.Text = isPaused ? "Start" : "Pause";
            labelStatus.Text = isPaused ? "Adatfeldolgozás szünetel..." : "Adatfeldolgozás folytatódik...";

            if (!isPaused)
            {
                counter = 0; // új szakasz indul
                dataGridView1.Rows.Clear(); // előző mérések törlése, ha nem lettek elmentve
            }

            if (!isPaused)
            {
                counter = 0;
                dataGridView1.Rows.Clear();

                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.DiscardInBuffer();
                    System.Threading.Thread.Sleep(200); // 200 ms várakozás
                    serialPort.DiscardInBuffer(); // biztos, ami biztos
                }
            }


        }
        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }
        private void loadFromFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "CSV fájlok (*.csv)|*.csv|Összes fájl (*.*)|*.*",
                Title = "Válaszd ki a betöltendő tanítási adatfájlt"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string path = openFileDialog.FileName;

                try
                {
                    var lines = File.ReadAllLines(path);
                    trainingData.Clear();

                    foreach (var line in lines)
                    {
                        var parts = line.Split('\t');
                        if (parts.Length == 8)
                        {
                            var data = new LightSensorData
                            {
                                Volt_R = float.Parse(parts[1], new CultureInfo("hu-HU")),
                                Volt_O = float.Parse(parts[2], new CultureInfo("hu-HU")),
                                Volt_Y = float.Parse(parts[3], new CultureInfo("hu-HU")),
                                Volt_G = float.Parse(parts[4], new CultureInfo("hu-HU")),
                                Volt_B = float.Parse(parts[5], new CultureInfo("hu-HU")),
                                Volt_U = float.Parse(parts[6], new CultureInfo("hu-HU")),
                                Color = parts[7]
                            };
                            trainingData.Add(data);
                        }
                    }

                    labelStatus.Text = $"Beolvasva {trainingData.Count} minta a fájlból: {Path.GetFileName(path)}";
                    UpdateStatistics();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hiba történt a fájl beolvasása közben: " + ex.Message);
                }
            }
        }
        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
          
                if (trainingData.Count == 0)
                {
                    MessageBox.Show("Nincs menthető adat! Előbb ments el mintákat vagy tölts be fájlt.");
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV fájlok (*.csv)|*.csv|Összes fájl (*.*)|*.*",
                    Title = "Mentés más néven",
                    FileName = $"training_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName, false))
                        {
                            foreach (var data in trainingData)
                            {
                                string line = string.Join("\t", new string[]
                                {
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        data.Volt_R.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Volt_O.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Volt_Y.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Volt_G.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Volt_B.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Volt_U.ToString("0.000", new CultureInfo("hu-HU")),
                        data.Color
                                });

                                writer.WriteLine(line);
                            }
                        }

                        labelStatus.Text = $"Mentés kész: {Path.GetFileName(saveFileDialog.FileName)}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Hiba történt a fájl mentése közben: " + ex.Message);
                    }
                }
           

        }
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Opcionális: megerősítés
            var result = MessageBox.Show("Biztosan ki szeretnél lépni?", "Kilépés megerősítése",
                                         MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    // Ha nyitva van a soros port, zárjuk le
                    if (serialPort != null && serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hiba a COM port lezárásakor: " + ex.Message);
                }

                Application.Exit();
            }
        }
        private void panelColorCircle_Paint(object sender, PaintEventArgs e)
        {
           
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (Brush brush = new SolidBrush(currentPredictionColor))
                {
                    g.FillEllipse(brush, 0, 0, panelColorCircle.Width - 1, panelColorCircle.Height - 1);
                }

                using (Pen pen = new Pen(Color.DarkGray, 2))
                {
                    g.DrawEllipse(pen, 0, 0, panelColorCircle.Width - 1, panelColorCircle.Height - 1);
                }
           
        }
    }
        public class LightSensorData
    {
        [LoadColumn(0)] public float Volt_R;
        [LoadColumn(1)] public float Volt_O;
        [LoadColumn(2)] public float Volt_Y;
        [LoadColumn(3)] public float Volt_G;
        [LoadColumn(4)] public float Volt_B;
        [LoadColumn(5)] public float Volt_U;
        [LoadColumn(6)] public string Color;
    }
        public class LightSensorPrediction
    {
        public string PredictedColor { get; set; }

        [ColumnName("Score")]
        public float[] Score { get; set; }

        [ColumnName("PredictedLabel")]
        public uint PredictedLabelIndex { get; set; }  // 1-alapú index!
    }

}

