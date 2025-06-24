using ClickableTransparentOverlay;
using FFTW.NET;
using ImGuiNET;
using MathNet.Numerics;
using NAudio.Wave;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;

namespace InstrumentTuner
{
    public class Main : Overlay
    {
        private WaveInEvent device;
        private static int selectedMic = 0;
        private static string[] availableMicrophones = new string[0];

        public Main(Size size) : base(size.Width, size.Height)
        {
            base.VSync = true;
            refreshMicrophones();
            initiateDevice();
        }

        protected override Task PostInitialized()
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600));
            return Task.CompletedTask;
        }

        private void initiateDevice()
        {
            int[] testRates = { 8000,
    11025,
    16000,
    22050,
    32000,
    44100,
    48000,
    88200,
    96000,
            176400 ,
            192000 ,
            384000 };
            testRates = testRates.Reverse().ToArray();
            sampleRate = 0;
            foreach (var rate in testRates)
            {
                try
                {
                    using (var waveIn = new WaveInEvent())
                    {
                        waveIn.DeviceNumber = selectedMic;
                        waveIn.WaveFormat = new WaveFormat(rate, 1);
                        waveIn.DataAvailable += (s, a) => { };
                        waveIn.StartRecording();
                        waveIn.StopRecording();
                        Console.WriteLine($"Supported Maximum Sample-rate: {rate} Hz");
                        sampleRate = rate;
                        break;
                    }
                }
                catch
                {
                    //not a supported sample rate
                }
            }
            if (sampleRate == 0)
            {
                Console.WriteLine("No Supported sample rate found, goofy ah soundcard");
                return;
            }
            device = new WaveInEvent();
            device.WaveFormat = new WaveFormat(sampleRate, 1); // 44.1kHz mono
            device.DataAvailable += WaveInOnDataAvailable;
            device.DeviceNumber = selectedMic;
            initFFTPlain();
            device.StartRecording();
        }

        private void refreshMicrophones()
        {
            availableMicrophones = new string[WaveInEvent.DeviceCount];
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var cap = WaveInEvent.GetCapabilities(i);
                availableMicrophones[i] = cap.ProductName;
            }
        }

        private static int sampleRate = 44100;
        private static float frequencyResolution = 0.5f;
        private static int bufferSize = (int)Math.Ceiling(sampleRate / frequencyResolution);
        private static FftwArrayDouble fftInput = new FftwArrayDouble(bufferSize);
        private static FftwArrayComplex fftOutput = new FftwArrayComplex(bufferSize / 2 + 1);
        private static FftwPlanRC plain = FftwPlanRC.Create(fftInput, fftOutput, DftDirection.Forwards);

        private static void initFFTPlain()
        {
            frequencyResolution = 0.5f;//half hz resolution
            bufferSize = (int)Math.Ceiling(sampleRate / frequencyResolution);
            plain.Dispose();
            fftInput.Dispose();
            fftOutput.Dispose();
            fftInput = new FftwArrayDouble(bufferSize);
            fftOutput = new FftwArrayComplex(bufferSize / 2 + 1);
            Console.WriteLine($"Creating FFTW plan (might take a while)....");
            plain = FftwPlanRC.Create(fftInput, fftOutput, DftDirection.Forwards, PlannerFlags.Estimate);
            Console.WriteLine($"Done....");
            initBinTable();
        }

        public static List<(int w1, int w2)> GetRanges(List<int> points)
        {
            var ranges = new List<(int w1, int w2)>();

            for (int i = 1; i < points.Count - 1; i++)
            {
                int x = points[i - 1];
                int y = points[i];
                int z = points[i + 1];

                // Basic range
                int w1 = (x + y) / 2;
                int w2 = (y + z) / 2;

                // Ensure no overlap
                if (ranges.Count > 0 && w1 <= ranges[^1].w2)
                {
                    w1 = ranges[^1].w2 + 1;
                }

                ranges.Add((w1, w2));
            }

            return ranges;
        }

        private static void initBinTable()
        {
            var notes = notesChart.Keys.ToArray();
            for (int row = 0; row < notes.Length; row++)
            {
                var note = notes[row];
                var octavesFrequencies = notesChart[note].Item1;
                List<int> octaveBins = new List<int>();
                octavesFrequencies.ToList().ForEach(x => octaveBins.Add(freqToBin(bufferSize, sampleRate, x)));
                notesChart[note] = new Tuple<float[], int[]?, int[][]?>(octavesFrequencies, octaveBins.ToArray(), null);
            }
            for (int col = 0; col < notesChart[notes[0]].Item2.Length; col++)
            {
                for (int row = 0; row < notes.Length; row++)
                {
                    var baseBin = notesChart[notes[row]].Item2[col];
                    float baseFreq = notesChart[notes[row]].Item1[col];
                    int adjecentPosBin = baseBin;
                    int adjecentNegBin = baseBin;
                    try
                    {
                        int nextRow = (row + 1);
                        if (nextRow == notes.Length)
                            adjecentPosBin = baseBin + 2;
                        else
                            adjecentPosBin = notesChart[notes[nextRow]].Item2[
                                (col + nextRow / notes.Length) % notesChart[notes[row]].Item2.Length];

                        int prevRow = (row - 1) % notes.Length;
                        if (prevRow == -1)
                            adjecentNegBin = baseBin - 2;
                        else
                            adjecentNegBin = notesChart[notes[prevRow]].Item2[
                                (col + (prevRow) / notes.Length) % notesChart[notes[row]].Item2.Length];
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error bound ->\n" +
                            $"Pos {(row + 1) % notes.Length},{(col + (row + 1) / notes.Length) % notesChart[notes[row]].Item2.Length}\n" +
                            $"Neg:{(row - 1) % notes.Length},{(col + (row - 1) / notes.Length) % notesChart[notes[row]].Item2.Length}");
                        //a wrap of the array
                    }
                    var min = (baseBin + adjecentNegBin) / 2 + 1;
                    var max = (baseBin + adjecentPosBin) / 2;

                    int[][]? binTables = notesChart[notes[row]].Item3;
                    if (binTables is null)
                        binTables = new int[notesChart[notes[row]].Item1.Length][];
                    binTables[col] = new int[] { min, max };
                    notesChart[notes[row]] = new Tuple<float[], int[]?, int[][]?>(notesChart[notes[row]].Item1, notesChart[notes[row]].Item2, binTables);
                    Console.Write($"{{{min},{max}}}, ");
                }
                Console.WriteLine("");
            }
        }

        public static float GaussianWeightedAverage(float[] data)
        {
            int N = data.Length;

            // Create weights
            double[] weights = MathNet.Numerics.Window.Gauss(data.Length, 0.5);
            float weightSum = 0.0f;
            for (int i = 0; i < N; i++)
            {
                weightSum += (float)weights[i];
            }

            // Perform weighted average
            float result = 0.0f;
            for (int i = 0; i < N; i++)
            {
                result += data[i] * (float)weights[i];
            }

            return result / weightSum;
        }

        public static float GetGaussianShift(float[] values)
        {
            var Sum = values.Sum();
            var pendolom = 0.0f;
            var center = values.Length / 2 + 1;
            for (int i = 0; i < values.Length; i++)
            {
                pendolom += (float)(i - center) * (values[i] / Sum);
            }
            return pendolom;
        }

        private static int freqToBin(int length, float SR, float frequency)
        {
            return (int)Math.Round(frequency * length / (SR));
        }

        private static int binToFreq(int length, float SR, float bin)
        {
            return (int)Math.Round(bin * SR / (length));
        }

        //note,octave[],octave Freq as bin[],int[] range of bins to sum
        private static Dictionary<string, Tuple<float[], int[]?, int[][]?>> notesChart = new Dictionary<string, Tuple<float[], int[]?, int[][]?>>
{
    // taken from https://mixbutton.com/music-tools/frequency-and-pitch/music-note-to-frequency-chart
    { "C",    new Tuple<float[],int[]?,int[][]?>(new float[] { 32.70f, 65.41f, 130.81f, 261.63f, 523.25f, 1046.50f, 2093.00f, 4186.01f } ,null,null)},
    { "C#/Db",new Tuple<float[],int[]?,int[][]?>(new float[] { 34.65f, 69.30f, 138.59f, 277.18f, 554.37f, 1108.73f, 2217.46f, 4434.92f } ,null,null)},
    { "D",    new Tuple<float[],int[]?,int[][]?>(new float[] { 36.71f, 73.42f, 146.83f, 293.66f, 587.33f, 1174.66f, 2349.32f, 4698.63f },null ,null)},
    { "D#/Eb",new Tuple<float[],int[]?,int[][]?>(new float[] { 38.89f, 77.78f, 155.56f, 311.13f, 622.25f, 1244.51f, 2489.02f, 4978.03f },null,null) },
    { "E",    new Tuple<float[],int[]?,int[][]?>(new float[] { 41.20f, 82.41f, 164.81f, 329.63f, 659.25f, 1318.51f, 2637.02f, 5274.04f },null,null) },
    { "F",    new Tuple<float[],int[]?,int[][]?>(new float[] { 43.65f, 87.31f, 174.61f, 349.23f, 698.46f, 1396.91f, 2793.83f, 5587.65f },null,null) },
    { "F#/Gb",new Tuple<float[],int[]?,int[][]?>(new float[] { 46.25f, 92.50f, 185.00f, 369.99f, 739.99f, 1479.98f, 2959.96f, 5919.91f },null,null) },
    { "G",    new Tuple<float[],int[]?,int[][]?>(new float[] { 49.00f, 98.00f, 196.00f, 392.00f, 783.99f, 1567.98f, 3135.96f, 6271.93f },null,null) },
    { "G#/Ab",new Tuple<float[],int[]?,int[][]?>(new float[] { 51.91f, 103.83f, 207.65f, 415.30f, 830.61f, 1661.22f, 3322.44f, 6644.88f },null ,null)},
    { "A",    new Tuple<float[],int[]?,int[][]?>(new float[] { 55.00f, 110.00f, 220.00f, 440.00f, 880.00f, 1760.00f, 3520.00f, 7040.00f },null,null) },
    { "A#/Bb",new Tuple<float[],int[]?,int[][]?>(new float[] { 58.27f, 116.54f, 233.08f, 466.16f, 932.33f, 1864.66f, 3729.31f, 7458.62f },null,null) },
    { "B",    new Tuple<float[],int[]?,int[][]?>(new float[] { 61.74f, 123.47f, 246.94f, 493.88f, 987.77f, 1975.53f, 3951.07f, 7902.13f },null,null) }
};

        private static int refreshMS = 1;

        private static Stopwatch stopwatch = new Stopwatch();
        private static string notesScores = string.Empty;
        private static float drift = 0;

        //key,row,col
        private static Tuple<string, int, int> bestNote = new Tuple<string, int, int>(string.Empty, 0, 0);

        private static void findBestNote()
        {
            var keys = notesChart.Keys.ToArray();
            int bestNoteBin = 0;
            Tuple<string, int, int> bestNote = new Tuple<string, int, int>(string.Empty, 0, 0);
            float bestScore = float.MinValue;
            string noteResults = string.Empty;
            //detect most loud note
            for (int row = 0; row < keys.Length; row++)
            {
                string key = keys[row];
                for (int col = 0; col < notesChart[keys[row]].Item2.Length; col++)
                {
                    int[] range = notesChart[keys[row]].Item3[col];
                    List<float> values = new List<float>();
                    for (int bin = notesChart[keys[row]].Item3[col][0]; bin <= notesChart[keys[row]].Item3[col][1]; bin++)
                    {
                        values.Add(graphbufferAveraged[bin]);
                    }
                    var score = GaussianWeightedAverage(values.ToArray());
                    if (score > bestScore)
                    {
                        bestNote = new Tuple<string, int, int>($"{key}{col}".ToUpper(), row, col);
                        bestNoteBin = notesChart[keys[row]].Item2[col];
                        bestScore = score;
                    }
                    noteResults += $"{key}{col}, {score}\n".ToUpper();
                }
            }
            if (bestScore <= 50) //small noise, can barley hear it
                return;
            notesScores = noteResults;
            Main.bestNote = bestNote;
            //detect drift from actual note
            float[] GaussShift = new float[] {
                graphbufferAveraged[bestNoteBin - 4], graphbufferAveraged[bestNoteBin -3],
                graphbufferAveraged[bestNoteBin - 2], graphbufferAveraged[bestNoteBin -1],
                graphbufferAveraged[bestNoteBin],
                graphbufferAveraged[bestNoteBin+1],graphbufferAveraged[bestNoteBin+2],
            graphbufferAveraged[bestNoteBin+3],graphbufferAveraged[bestNoteBin+4]};
            drift = 1 + GetGaussianShift(GaussShift);
            if (drift > 0)
                Console.WriteLine($"{bestNote}|+{drift}");
            else
                Console.WriteLine($"{bestNote}|{drift}");
        }

        private static int requiredBuffer = 0;

        private static void WaveInOnDataAvailable(object sender, WaveInEventArgs e)
        {
            var newSampleslength = e.BytesRecorded / 2;
            //shifting the samples
            /*for (int i = newSampleslength; i < fftInput.Length; i++)
            {
                fftInput[i - newSampleslength] = fftInput[i];
            }*/
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                fftInput[requiredBuffer++] = sample / 32768.0;
            }
            if (requiredBuffer >= 10000)
            //if (stopwatch.ElapsedMilliseconds >= refreshMS)
            {
                //do not change a device while i am executing FFT
                plain.Execute();
                updateFFTData();
                findBestNote();
                for (int i = 0; i < fftInput.Length; i++)
                {
                    fftInput[i] = 0;
                }
                requiredBuffer = 0;
                stopwatch.Restart();
            }
        }

        private static RectangleF graph = new RectangleF(new PointF(0, 20), new SizeF(300, 300));

        private static int getFreqBin(int length, float SR, float frequency)
        {
            return (int)Math.Round(frequency * length / (SR));
        }

        private static double Scale(double value, double oldMin, double oldMax, double newMin, double newMax)
        {
            return newMin + (value - oldMin) * (newMax - newMin) / (oldMax - oldMin);
        }

        private static Vector2 scaleToGraph(float left, float top, float right, float bottom, float freq, float dB,
        double freqStart, double freqStop, double graph_startDB, double graph_endDB)
        {
            var scaledX = Scale(freq, freqStart, freqStop, left, right);
            //endb = 0

            var scaledY = Scale(dB, graph_startDB, graph_endDB, bottom, top);
            return new Vector2((float)scaledX, (float)scaledY);
        }

        //to make a smoother fft, i dont want to implement welching
        private static float[] graphbufferAveraged = new float[fftOutput.Length];

        private static int averages = 0;
        private bool isNewData = false;
        private static float[] graphbuffer = new float[fftOutput.Length];
        private static float mindB = 999, maxdB = -999;

        // +- 5bins for some space
        private static int minBin, maxBin;

        private static void updateFFTData()
        {
            minBin = notesChart[notesChart.Keys.First()].Item2.First() - 5;
            maxBin = notesChart[notesChart.Keys.Last()].Item2.Last() + 5;
            mindB -= 3;
            maxdB += 3;
            for (int i = minBin; i < maxBin; i++)
            {
                graphbuffer[i] = 10.0f * (float)Math.Log(fftOutput[i].MagnitudeSquared());
                graphbufferAveraged[i] = ((graphbufferAveraged[i] * averages) + graphbuffer[i]) / (averages + 1.0f);

                if (graphbuffer[i] <= mindB)
                    mindB = graphbuffer[i];
                if (graphbuffer[i] >= maxdB)
                    maxdB = graphbuffer[i];
            }
            averages++;
            if (averages == 10)
            {
                averages--;
            }
        }

        private static void drawFFT()
        {
            ImGui.Begin($"{availableMicrophones[selectedMic]} - FFT");
            var draw = ImGui.GetWindowDrawList();
            var size = ImGui.GetWindowSize();
            var pos = ImGui.GetWindowPos();
            var A = scaleToGraph(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y, 0, graphbuffer[0], minBin, maxBin, mindB, maxdB);
            for (int i = minBin; i < maxBin; i++)
            {
                var B = scaleToGraph(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y, i, graphbuffer[i], minBin, maxBin, mindB, maxdB);
                draw.AddLine(A, B, 0xFFFFFFFF);
                A = B;
            }
            A = scaleToGraph(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y, 0, graphbufferAveraged[0], minBin, maxBin, mindB, maxdB);
            for (int i = minBin; i < maxBin; i++)
            {
                var B = scaleToGraph(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y, i, graphbufferAveraged[i], minBin, maxBin, mindB, maxdB);
                draw.AddLine(A, B, 0XFFFF0000);
                A = B;
            }
            ImGui.End();
        }

        private static void drawScores()
        {
            ImGui.Begin($"Notes Scores");
            var text = notesScores.Split('\n');
            for (int i = 0; i < text.Length; i++)
            {
                ImGui.Text(text[i]);
                if ((i + 1) % 8 != 0)
                    ImGui.SameLine();
            }

            ImGui.End();
        }

        public static (float Y1, float Y2) GetYValuesOnCircle(float Cx, float Cy, float R, float Xp)
        {
            float dx = Xp - Cx;
            float discriminant = R * R - dx * dx;

            if (discriminant < 0)
            {
                // No intersection
                return (float.NaN, float.NaN);
            }

            float sqrt = MathF.Sqrt(discriminant);
            float Y1 = Cy + sqrt;
            float Y2 = Cy - sqrt;

            return (Y1, Y2);
        }

        private static void drawRotatingPointer()
        {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var draw = ImGui.GetWindowDrawList();
            string adjecentPos = "-", adjecentNeg = "-";
            var notes = notesChart.Keys.ToArray();
            int row = bestNote.Item2, col = bestNote.Item3;
            int nextRow = (row + 1);
            if (nextRow != notes.Length)
            {
                adjecentPos = notes[nextRow];
                adjecentPos += $"{(col + (nextRow) / notes.Length) % notesChart[notes[row]].Item2.Length}";
                adjecentPos = adjecentPos.ToUpper();
            }
            int prevRow = (row - 1) % notes.Length;
            if (prevRow != -1)
            {
                adjecentNeg = notes[prevRow];
                adjecentNeg += $"{(col + (prevRow) / notes.Length) % notesChart[notes[row]].Item2.Length}";
                adjecentNeg = adjecentPos.ToUpper();
            }
            var textPos = pos + size / 2 - ImGui.CalcTextSize(bestNote.Item1) / 2;
            draw.AddText(textPos, 0xFFFFFFFF, bestNote.Item1);
            var pointerPosStart = pos + size - new Vector2(size.X / 2, 0);
            var radius = Vector2.Distance(pointerPosStart, textPos + new Vector2(0, -10));
            draw.AddCircle(pointerPosStart, radius, 0xFFFFFFFF, 800);
            float left = pos.X, right = pos.X + size.X;
            float pointerXPos = textPos.X + (left - right) / 2 * ((drift) / 1.0f);
            draw.AddText(new Vector2(pos.X, GetYValuesOnCircle(pointerPosStart.X, pointerPosStart.Y, radius, pos.X).Y2), 0xFFFFFFFF, adjecentPos);
            draw.AddText(new Vector2(pos.X + size.X, GetYValuesOnCircle(pointerPosStart.X, pointerPosStart.Y, radius, pos.X + size.X).Y2)
                - new Vector2(ImGui.CalcTextSize(adjecentNeg).X, 0), 0xFFFFFFFF, adjecentNeg);

            draw.AddLine(pointerPosStart, new Vector2(pointerXPos, GetYValuesOnCircle(pointerPosStart.X, pointerPosStart.Y, radius, pointerXPos).Y2), 0xFFFFFFFF, 2.0f);
        }

        private bool showFFT = false, showScores = false;
        private static bool initialized = false;

        protected override void Render()
        {
            if (!initialized)
            {
                stopwatch.Restart();
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600));
                initialized = true;
            }
            ImGui.Begin("Instrument Tuner", ImGuiWindowFlags.NoResize);
            ImGui.Text("Select Microphone:");
            ImGui.SameLine();
            if (ImGui.Combo("microphones", ref selectedMic, availableMicrophones, availableMicrophones.Length))
            {
                device.StopRecording();
                device.Dispose();
                initiateDevice();
            }
            if (ImGui.Checkbox("Show FFT Window", ref showFFT))
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600));

            if (showFFT)
                drawFFT();

            if (ImGui.Checkbox("Show Scores Window", ref showScores))
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600));
            if (showScores)
                drawScores();
            drawRotatingPointer();
            ImGui.End();
        }
    }
}