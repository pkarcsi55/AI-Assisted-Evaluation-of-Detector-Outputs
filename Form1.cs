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
        private float[] latestVoltages = new float[6];//Detektrojelek
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
        private void panelBarGraph_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(panelBarGraph.BackColor);

            Color[] colors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Violet };
            string[] labels = { "R", "O", "Y", "G", "B", "U" };

            int barWidth = panelBarGraph.Width / latestVoltages.Length;
            float maxVoltage = 2.0f; // vagy amit jellemzően maximumként mérsz

            for (int i = 0; i < latestVoltages.Length; i++)
            {
                float value = Math.Min(latestVoltages[i], maxVoltage);
                int barHeight = (int)(value / maxVoltage * panelBarGraph.Height);

                Rectangle rect = new Rectangle(i * barWidth, panelBarGraph.Height - barHeight, barWidth - 4, barHeight);
                using (Brush brush = new SolidBrush(colors[i]))
                {
                    g.FillRectangle(brush, rect);
                }

                // Opcionális: érték felirat
                // string text = $"{labels[i]}\n{value:0.00}";
                // TextRenderer.DrawText(g, text, this.Font, new Point(i * barWidth + 2, panelBarGraph.Height - barHeight - 30), Color.Black);
            }
        }
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

            string statsText = $"Total samples: {trainingData.Count}\n";
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
            panelBarGraph.Paint += panelBarGraph_Paint;

            comboBoxAlgorithm.Items.AddRange(new string[]
                {
                "Maximum Entropy",
                "SDCA Maximum Entropy",
                "FastTree (One-vs-All)"
                });

            comboBoxAlgorithm.SelectedIndex = 0;

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
                labelStatus.Text = "Data processing paused (after opening a new COM port)";
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
                        latestVoltages = new float[] { vR, vO, vY, vG, vB, vU };
                        panelBarGraph.Invalidate(); // újrarajzolás

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
                            labelStatus.Text = $"Prediction: {prediction.PredictedColor} ({counter}/{maxSamples})";

                            UpdateColorCircle(prediction.PredictedColor); // színes kör frissítés
                        }
                        else
                        {
                            labelStatus.Text = $"Sample {counter}/{maxSamples} recorded.";
                            labelPredictionDetails.Text = "";
                            label1.Text = "";
                        }

                        if (counter >= maxSamples)
                        {
                            isPaused = true;
                            buttonPauseResume.Text = "Start";
                            labelStatus.Text = "15 samples recorded. Press Start to begin a new measurement sequence.";

                            if (serialPort != null && serialPort.IsOpen)
                            {
                                serialPort.DiscardInBuffer();
                            }
                        }
                    });
                }
                else
                {
                    Invoke((MethodInvoker)(() => labelStatus.Text = "Invalid data: expected 6 values."));
                }
            }
            catch
            {
                Invoke((MethodInvoker)(() => labelStatus.Text = "Invalid data received."));
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
                labelStatus.Text = $"Saved {dataGridView1.Rows.Count - 1} samples as '{comboBoxColor.SelectedItem}'. Total: {trainingData.Count} samples.";
                dataGridView1.Rows.Clear();
                UpdateStatistics(); // statisztikát is frissítjük
            }
            else
            {
                MessageBox.Show("No data to save. At least one complete row is required.");
            }
        }


        private void buttonPauseResume_Click(object sender, EventArgs e)
        {
            isPaused = !isPaused;

            buttonPauseResume.Text =
                isPaused ? "Start" : "Pause";

            labelStatus.Text =
                isPaused
                    ? "Data processing paused..."
                    : "Data processing running...";

            if (!isPaused)
            {
                counter = 0;
                dataGridView1.Rows.Clear();

                if (serialPort != null &&
                    serialPort.IsOpen)
                {
                    serialPort.DiscardInBuffer();

                    System.Threading.Thread.Sleep(200);

                    serialPort.DiscardInBuffer();
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
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Select the training data file to load"
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

                    labelStatus.Text = $"Loaded {trainingData.Count} samples from file: {Path.GetFileName(path)}";
                    UpdateStatistics();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error while reading the file: " + ex.Message);
                }
            }
        }
        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (trainingData.Count == 0)
            {
                MessageBox.Show("No data to save. First save samples or load a data file.");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Save As",
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

                    labelStatus.Text = $"Save completed: {Path.GetFileName(saveFileDialog.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error while saving the file: " + ex.Message);
                }
            }


        }
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Opcionális: megerősítés
            var result = MessageBox.Show("Are you sure you want to exit?", "Confirm exit",
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
                    MessageBox.Show("Error while closing the COM port: " + ex.Message);
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
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("LED PhotoEffect AI\nVersion 1.0.0\n© Piláth 2025\nhttps://github.com/pkarcsi55/AI-Assisted-Evaluation-of-Detector-Outputs", "About");
        }

        private void buttonTrain_Click(object sender, EventArgs e)
        {

            if (trainingData.Count < 10)
            {
                MessageBox.Show(
                    "Too little training data. Please collect more samples.");
                return;
            }

            // ----------------------------------------------------
            // Load the complete dataset
            // ----------------------------------------------------

            var dataView =
                mlContext.Data.LoadFromEnumerable(trainingData);

            // ----------------------------------------------------
            // Split dataset: 80% training / 20% testing
            // ----------------------------------------------------

            var split =
                mlContext.Data.TrainTestSplit(
                    dataView,
                    testFraction: 0.2,
                    seed: 1);

            // ----------------------------------------------------
            // Common data preprocessing pipeline
            // ----------------------------------------------------

            var prePipeline =
                mlContext.Transforms.Conversion.MapValueToKey(
                    "Label",
                    nameof(LightSensorData.Color))

                .Append(
                    mlContext.Transforms.Concatenate(
                        "Features",
                        nameof(LightSensorData.Volt_R),
                        nameof(LightSensorData.Volt_O),
                        nameof(LightSensorData.Volt_Y),
                        nameof(LightSensorData.Volt_G),
                        nameof(LightSensorData.Volt_B),
                        nameof(LightSensorData.Volt_U)))

                .Append(
                    mlContext.Transforms.NormalizeMinMax(
                        "Features"));

            // ----------------------------------------------------
            // Read selected learning algorithm
            // ----------------------------------------------------

            string selectedAlgorithm =
                comboBoxAlgorithm.SelectedItem != null
                    ? comboBoxAlgorithm.SelectedItem.ToString()
                    : "Maximum Entropy";

            IEstimator<ITransformer> trainer;

            // ----------------------------------------------------
            // Select trainer
            // ----------------------------------------------------

            switch (selectedAlgorithm)
            {
                case "SDCA Maximum Entropy":

                    trainer =
                        mlContext.MulticlassClassification.Trainers
                        .SdcaMaximumEntropy();

                    break;


                case "FastTree (One-vs-All)":

                    var binaryTrainer =
                        mlContext.BinaryClassification.Trainers
                        .FastTree();

                    trainer =
                        mlContext.MulticlassClassification.Trainers
                        .OneVersusAll(binaryTrainer);

                    break;


                case "Maximum Entropy":
                default:

                    trainer =
                        mlContext.MulticlassClassification.Trainers
                        .LbfgsMaximumEntropy();

                    break;
            }

            // ----------------------------------------------------
            // Build the complete ML.NET pipeline
            // ----------------------------------------------------

            var pipeline =
                prePipeline

                .Append(trainer)

                .Append(
                    mlContext.Transforms.Conversion.MapKeyToValue(
                        outputColumnName:
                            nameof(LightSensorPrediction.PredictedColor),

                        inputColumnName:
                            "PredictedLabel"));

            // ----------------------------------------------------
            // Train the model using only the training dataset
            // ----------------------------------------------------

            trainedModel =
                pipeline.Fit(split.TrainSet);

            predictor =
                mlContext.Model.CreatePredictionEngine
                <LightSensorData, LightSensorPrediction>(
                    trainedModel);

            // ----------------------------------------------------
            // Evaluate the model using previously unseen test data
            // ----------------------------------------------------

            var predictions =
                trainedModel.Transform(split.TestSet);

            var metrics =
                mlContext.MulticlassClassification.Evaluate(
                    predictions,
                    labelColumnName: "Label",
                    predictedLabelColumnName: "PredictedLabel");

            // ----------------------------------------------------
            // Read class names in the exact order used by ML.NET
            // ----------------------------------------------------

            VBuffer<ReadOnlyMemory<char>> keyValues = default;

            predictions.Schema["Label"]
                .GetKeyValues(ref keyValues);

            labelNames =
                keyValues
                .DenseValues()
                .Select(x => x.ToString())
                .ToArray();

            // ----------------------------------------------------
            // Show evaluation window
            // ----------------------------------------------------

            ShowModelEvaluation(
                metrics,
                trainingData.Count,
                labelNames,
                selectedAlgorithm);

            // ----------------------------------------------------
            // Update application status
            // ----------------------------------------------------

            labelStatus.Text =
                $"Model trained: {selectedAlgorithm} | " +
                $"Test accuracy: {metrics.MicroAccuracy * 100:0.0}%";

            UpdateModelStatus();
        }


        private void ShowModelEvaluation(
            MulticlassClassificationMetrics metrics,
            int totalSamples,
            string[] labels,
            string algorithm)
        {
            // ----------------------------------------------------
            // Create evaluation window
            // ----------------------------------------------------

            Form form = new Form();

            form.Text = "Model evaluation";
            form.Icon = this.Icon;
            form.StartPosition = FormStartPosition.CenterParent;
            form.Size = new Size(900, 680);
            form.MinimumSize = new Size(750, 530);

            form.FormBorderStyle =
                FormBorderStyle.Sizable;

            form.MaximizeBox = true;
            form.MinimizeBox = false;

            // ----------------------------------------------------
            // Main layout
            // ----------------------------------------------------

            TableLayoutPanel mainPanel =
                new TableLayoutPanel();

            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            mainPanel.ColumnCount = 1;
            mainPanel.RowCount = 7;

            mainPanel.RowStyles.Clear();

            // Title
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            // Algorithm
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            // Sample information
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            // Metrics
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            // Confusion matrix title
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            // Confusion matrix grid - receives all remaining space
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));

            // Close button
            mainPanel.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            form.Controls.Add(mainPanel);

            // ----------------------------------------------------
            // Window title
            // ----------------------------------------------------

            Label title = new Label();

            title.Text = "Model evaluation";
            title.Font =
                new Font("Segoe UI", 20, FontStyle.Bold);

            title.AutoSize = true;
            title.Margin =
                new Padding(0, 0, 0, 8);

            mainPanel.Controls.Add(title, 0, 0);

            // ----------------------------------------------------
            // Selected algorithm
            // ----------------------------------------------------

            Label algorithmInfo = new Label();

            algorithmInfo.Text =
                $"Algorithm: {algorithm}";

            algorithmInfo.Font =
                new Font("Segoe UI", 11, FontStyle.Bold);

            algorithmInfo.AutoSize = true;
            algorithmInfo.Margin =
                new Padding(0, 0, 0, 6);

            mainPanel.Controls.Add(
                algorithmInfo, 0, 1);

            // ----------------------------------------------------
            // Sample information
            // ----------------------------------------------------

            Label sampleInfo = new Label();

            sampleInfo.Text =
                $"Total samples: {totalSamples}     " +
                $"Training data: 80 %     " +
                $"Test data: 20 %";

            sampleInfo.Font =
                new Font("Segoe UI", 10);

            sampleInfo.AutoSize = true;
            sampleInfo.Margin =
                new Padding(0, 0, 0, 18);

            mainPanel.Controls.Add(
                sampleInfo, 0, 2);

            // ----------------------------------------------------
            // Metrics panel
            // ----------------------------------------------------

            TableLayoutPanel metricsPanel =
                new TableLayoutPanel();

            metricsPanel.AutoSize = false;
            metricsPanel.Height = 72;

            metricsPanel.ColumnCount = 3;
            metricsPanel.RowCount = 2;

            metricsPanel.Dock =
                DockStyle.Fill;

            metricsPanel.ColumnStyles.Clear();

            metricsPanel.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent, 33.333f));

            metricsPanel.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent, 33.333f));

            metricsPanel.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent, 33.334f));

            // ----------------------------------------------------
            // Metric titles
            // ----------------------------------------------------

            Label microTitle = new Label();

            microTitle.Text =
                "Micro accuracy";

            microTitle.Font =
                new Font("Segoe UI", 10);

            microTitle.TextAlign =
                ContentAlignment.MiddleCenter;

            microTitle.Dock =
                DockStyle.Fill;


            Label macroTitle = new Label();

            macroTitle.Text =
                "Macro accuracy";

            macroTitle.Font =
                new Font("Segoe UI", 10);

            macroTitle.TextAlign =
                ContentAlignment.MiddleCenter;

            macroTitle.Dock =
                DockStyle.Fill;


            Label lossTitle = new Label();

            lossTitle.Text =
                "Log loss";

            lossTitle.Font =
                new Font("Segoe UI", 10);

            lossTitle.TextAlign =
                ContentAlignment.MiddleCenter;

            lossTitle.Dock =
                DockStyle.Fill;

            // ----------------------------------------------------
            // Metric values
            // ----------------------------------------------------

            Label microValue = new Label();

            microValue.Text =
                $"{metrics.MicroAccuracy * 100:0.0} %";

            microValue.Font =
                new Font(
                    "Segoe UI",
                    18,
                    FontStyle.Bold);

            microValue.TextAlign =
                ContentAlignment.MiddleCenter;

            microValue.Dock =
                DockStyle.Fill;


            Label macroValue = new Label();

            macroValue.Text =
                $"{metrics.MacroAccuracy * 100:0.0} %";

            macroValue.Font =
                new Font(
                    "Segoe UI",
                    18,
                    FontStyle.Bold);

            macroValue.TextAlign =
                ContentAlignment.MiddleCenter;

            macroValue.Dock =
                DockStyle.Fill;


            Label lossValue = new Label();

            lossValue.Text =
                $"{metrics.LogLoss:0.000}";

            lossValue.Font =
                new Font(
                    "Segoe UI",
                    18,
                    FontStyle.Bold);

            lossValue.TextAlign =
                ContentAlignment.MiddleCenter;

            lossValue.Dock =
                DockStyle.Fill;

            // ----------------------------------------------------
            // Add metrics to panel
            // ----------------------------------------------------

            metricsPanel.Controls.Add(
                microTitle, 0, 0);

            metricsPanel.Controls.Add(
                macroTitle, 1, 0);

            metricsPanel.Controls.Add(
                lossTitle, 2, 0);

            metricsPanel.Controls.Add(
                microValue, 0, 1);

            metricsPanel.Controls.Add(
                macroValue, 1, 1);

            metricsPanel.Controls.Add(
                lossValue, 2, 1);

            metricsPanel.Margin =
                new Padding(0, 0, 0, 14);

            mainPanel.Controls.Add(
                metricsPanel, 0, 3);

            // ----------------------------------------------------
            // Confusion matrix title
            // ----------------------------------------------------

            Label matrixTitle = new Label();

            matrixTitle.Text =
                "Confusion matrix";

            matrixTitle.Font =
                new Font(
                    "Segoe UI",
                    13,
                    FontStyle.Bold);

            matrixTitle.AutoSize = true;
            matrixTitle.Margin =
                new Padding(0, 0, 0, 8);

            mainPanel.Controls.Add(
                matrixTitle, 0, 4);

            // ----------------------------------------------------
            // Confusion matrix DataGridView
            // ----------------------------------------------------

            DataGridView grid =
                new DataGridView();

            grid.Dock = DockStyle.Fill;

            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;

            grid.ReadOnly = true;

            grid.RowHeadersVisible = true;
            grid.RowHeadersWidth = 110;

            grid.BackgroundColor =
                Color.White;

            grid.BorderStyle =
                BorderStyle.FixedSingle;

            grid.SelectionMode =
                DataGridViewSelectionMode.CellSelect;

            grid.MultiSelect = false;

            grid.AutoSizeColumnsMode =
                DataGridViewAutoSizeColumnsMode.Fill;

            grid.AutoSizeRowsMode =
                DataGridViewAutoSizeRowsMode.None;

            grid.RowTemplate.Height = 30;

            grid.DefaultCellStyle.Font =
                new Font("Segoe UI", 10);

            grid.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleCenter;

            grid.ColumnHeadersDefaultCellStyle.Font =
                new Font(
                    "Segoe UI",
                    10,
                    FontStyle.Bold);

            grid.ColumnHeadersDefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleCenter;

            grid.RowHeadersDefaultCellStyle.Font =
                new Font(
                    "Segoe UI",
                    10,
                    FontStyle.Bold);

            grid.RowHeadersDefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleLeft;

            // ----------------------------------------------------
            // Add class columns
            // ----------------------------------------------------

            foreach (string label in labels)
            {
                DataGridViewTextBoxColumn column =
                    new DataGridViewTextBoxColumn();

                column.Name = label;
                column.HeaderText = label;

                column.SortMode =
                    DataGridViewColumnSortMode.NotSortable;

                grid.Columns.Add(column);
            }

            // ----------------------------------------------------
            // Fill confusion matrix
            // ----------------------------------------------------

            var matrix =
                metrics.ConfusionMatrix;

            for (int i = 0;
                 i < matrix.Counts.Count;
                 i++)
            {
                object[] values =
                    new object[
                        matrix.Counts[i].Count];

                for (int j = 0;
                     j < matrix.Counts[i].Count;
                     j++)
                {
                    values[j] =
                        matrix.Counts[i][j];
                }

                int rowIndex =
                    grid.Rows.Add(values);

                if (i < labels.Length)
                {
                    grid.Rows[rowIndex]
                        .HeaderCell.Value =
                        labels[i];
                }
            }

            // ----------------------------------------------------
            // Highlight correct and incorrect classifications
            // ----------------------------------------------------

            for (int i = 0;
                 i < matrix.Counts.Count;
                 i++)
            {
                for (int j = 0;
                     j < matrix.Counts[i].Count;
                     j++)
                {
                    double value =
                        matrix.Counts[i][j];

                    DataGridViewCell cell =
                        grid.Rows[i].Cells[j];

                    if (i == j)
                    {
                        // Correct classification
                        cell.Style.BackColor =
                            Color.FromArgb(
                                210, 245, 210);

                        cell.Style.Font =
                            new Font(
                                grid.Font,
                                FontStyle.Bold);
                    }
                    else if (value > 0)
                    {
                        // Incorrect classification
                        cell.Style.BackColor =
                            Color.FromArgb(
                                255, 205, 205);

                        cell.Style.ForeColor =
                            Color.DarkRed;

                        cell.Style.Font =
                            new Font(
                                grid.Font,
                                FontStyle.Bold);
                    }
                }
            }

            grid.ClearSelection();

            mainPanel.Controls.Add(
                grid, 0, 5);

            // ----------------------------------------------------
            // Bottom panel and Close button
            // ----------------------------------------------------

            FlowLayoutPanel bottomPanel =
                new FlowLayoutPanel();

            bottomPanel.Dock =
                DockStyle.Fill;

            bottomPanel.FlowDirection =
                FlowDirection.RightToLeft;

            bottomPanel.AutoSize = true;

            bottomPanel.Padding =
                new Padding(0, 12, 0, 0);

            Button closeButton =
                new Button();

            closeButton.Text = "Close";
            closeButton.Width = 100;
            closeButton.Height = 34;

            closeButton.Font =
                new Font("Segoe UI", 10);

            closeButton.Click +=
                (s, e) => form.Close();

            bottomPanel.Controls.Add(
                closeButton);

            mainPanel.Controls.Add(
                bottomPanel, 0, 6);

            // ----------------------------------------------------
            // Display window
            // ----------------------------------------------------

            form.AcceptButton =closeButton;

            form.Shown += (s, e) =>
            {
                grid.ClearSelection();
                grid.CurrentCell = null;
            };

            form.ShowDialog(this);

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

