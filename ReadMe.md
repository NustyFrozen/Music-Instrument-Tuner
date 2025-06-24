## Musical Instrument Tuner / audio note detector



https://github.com/user-attachments/assets/e8884f63-f34c-4a47-b06b-37a341fd55c4


![GUI](https://github.com/NustyFrozen/Music-Instrument-Tuner/blob/main/media/gui.png?raw=true)
how it works:
1. it records baseband audio samples from your microphone (0-20k hz)
2. the samples go through Fast Fourier Transform with a buffer length that gives 0.5Hz resolution in the frequency domain
3. the program looks up at FFT bins of notes and neighbouring bins (based on the attached table) and calculate gaussian average:
![https://mixbutton.com/music-tools/frequency-and-pitch/music-note-to-frequency-chart](https://github.com/NustyFrozen/Music-Instrument-Tuner/blob/main/media/image.png?raw=true))
4. it takes the highest average and check for the mean value of the bins and based on the mean's bin index it calculate deviation from the actual note and then displays it through ImGUI

## requirements
windows only (due to the GUI library)<br>
[.NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)<br>
a microphone / soundcard with high sample rate (low sample rate will delay note detection, you may lower FFT bin resolution in the function initFFTPlain() to account for that, but you wont be able to detect lower notes as their spacing is too small)<br>
something to consider if your microphone attenuates higher frequencies (something common) it may degrade note detection
<br><br>
## Libraries<br>
[FFTW](https://github.com/ArgusMagnus/FFTW.NET)<br>
[NAudio](https://github.com/naudio/NAudio)<br>
[Math.NET](https://numerics.mathdotnet.com/)<br>
[ImGui - ClickableTransparentOverlay](https://github.com/zaafar/ClickableTransparentOverlay)
