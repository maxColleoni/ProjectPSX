using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using NAudio.Wave;
using ProjectPSX.Devices.Input;
using ProjectPSX.Interop.Gdi32;
using ProjectPSX.Util;
using Gdi32 = ProjectPSX.Interop.Gdi32.NativeMethods;

namespace ProjectPSX {
    public class Window : Form, IHostWindow {

        private const int PSX_MHZ = 33868800;
        private const int SYNC_CYCLES = 100;
        private const int MIPS_UNDERCLOCK = 3;

        private const int cyclesPerFrame = PSX_MHZ / 60;
        private const int syncLoops = (cyclesPerFrame / (SYNC_CYCLES * MIPS_UNDERCLOCK)) + 1;
        private const int cycles = syncLoops * SYNC_CYCLES;
        // Clock cycles one RunFrame emulates (ProjectPSX.RunFrame ticks the bus
        // SYNC_CYCLES * MIPS_UNDERCLOCK + 1 per loop): 566482, about 1/59.79 s.
        private const long emulatedCyclesPerFrame = (long)syncLoops * (SYNC_CYCLES * MIPS_UNDERCLOCK + 1);

        private Size vramSize = new Size(1024, 512);
        private Size _640x480 = new Size(640, 480);
        private readonly DoubleBufferedPanel screen = new DoubleBufferedPanel();

        private GdiBitmap display = new GdiBitmap(1024, 512);

        private ProjectPSX psx;
        private int fps;
        private bool isVramViewer;

        private int horizontalRes;
        private int verticalRes;

        private int displayVRAMXStart;
        private int displayVRAMYStart;

        private bool is24BitDepth;

        private int displayX1;
        private int displayX2;
        private int displayY1;
        private int displayY2;

        private long cpuCyclesCounter;

        Dictionary<Keys, GamepadInputsEnum> _gamepadKeyMap;

        private WaveOutEvent waveOutEvent = new WaveOutEvent();
        private BufferedWaveProvider bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat());

        public Window() {
            Text = "ProjectPSX";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyUp += new KeyEventHandler(vramViewerToggle);

            screen.Size = _640x480;
            screen.Margin = new Padding(0);
            screen.MouseDoubleClick += new MouseEventHandler(toggleDebug);

            Controls.Add(screen);
            // Debug will crash if not:
            CheckForIllegalCrossThreadCalls = false;

            KeyDown += new KeyEventHandler(handleJoyPadDown);
            KeyUp += new KeyEventHandler(handleJoyPadUp);

            _gamepadKeyMap = new Dictionary<Keys, GamepadInputsEnum>() {
                { Keys.Space, GamepadInputsEnum.Space},
                { Keys.Z , GamepadInputsEnum.Z },
                { Keys.C , GamepadInputsEnum.C },
                { Keys.Enter , GamepadInputsEnum.Enter },
                { Keys.Up , GamepadInputsEnum.Up },
                { Keys.Right , GamepadInputsEnum.Right },
                { Keys.Down , GamepadInputsEnum.Down },
                { Keys.Left , GamepadInputsEnum.Left },
                { Keys.D1 , GamepadInputsEnum.D1 },
                { Keys.D3 , GamepadInputsEnum.D3 },
                { Keys.Q , GamepadInputsEnum.Q },
                { Keys.E , GamepadInputsEnum.E },
                { Keys.W , GamepadInputsEnum.W },
                { Keys.D , GamepadInputsEnum.D },
                { Keys.S , GamepadInputsEnum.S },
                { Keys.A , GamepadInputsEnum.A },
            };

            bufferedWaveProvider.DiscardOnBufferOverflow = true;
            bufferedWaveProvider.BufferDuration = TimeSpan.FromMilliseconds(500);
            waveOutEvent.DesiredLatency = 90;
            waveOutEvent.NumberOfBuffers = 3;
            waveOutEvent.Init(bufferedWaveProvider);

            string diskFilename = GetDiskFilename();
            psx = new ProjectPSX(this, diskFilename);

            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += OnTimedEvent;
            timer.Enabled = true;
        }

        // The emulation thread draws straight onto the screen control. Started
        // from the constructor, its first frame could come before the form had
        // its window, and the control's window was then created on the
        // emulation thread, which never pumps messages: the form never showed.
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            RunUncapped();
        }

        private string GetDiskFilename() {
            var cla = Environment.GetCommandLineArgs();
            if (cla.Any(s => s.EndsWith(".bin") || s.EndsWith(".cue") || s.EndsWith(".exe"))) {
                string filename = cla.First(s => s.EndsWith(".bin") || s.EndsWith(".cue") || s.EndsWith(".exe"));
                return filename;
            } else {
                //Show the user a dialog so they can pick the bin they want to load.
                var fileDialog = new OpenFileDialog();
                fileDialog.Filter = "BIN/CUE files or PSXEXEs(*.bin, *.cue, *.exe)|*.bin;*.cue;*.exe";
                fileDialog.ShowDialog();

                string file = fileDialog.FileName;
                return file;
            }
        }

        private void handleJoyPadUp(object sender, KeyEventArgs e) {
            GamepadInputsEnum? button = GetGamepadButton(e.KeyCode);
            if(button != null)
                psx.JoyPadUp(button.Value);
        }

        private GamepadInputsEnum? GetGamepadButton(Keys keyCode) {
            if (_gamepadKeyMap.TryGetValue(keyCode, out GamepadInputsEnum gamepadButtonValue))
                return gamepadButtonValue;
            return null;
        }

        private void handleJoyPadDown(object sender, KeyEventArgs e) {
            GamepadInputsEnum? button = GetGamepadButton(e.KeyCode);
            if (button != null)
                psx.JoyPadDown(button.Value);
        }

        private void toggleDebug(object sender, MouseEventArgs e) => psx.toggleDebug();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(int[] vram) {
            //Console.WriteLine($"x1 {displayX1} x2 {displayX2} y1 {displayY1} y2 {displayY2}");

            int horizontalEnd = horizontalRes;
            int verticalEnd = verticalRes;

            if (isVramViewer) {
                horizontalEnd = 1024;
                verticalEnd = 512;
                
                Marshal.Copy(vram, 0, display.BitmapData, 0x80000);
            } else if (is24BitDepth) {
                blit24bpp(vram);
            } else {
                blit16bpp(vram);
            }

            fps++;

            using var deviceContext = new GdiDeviceContext(screen.Handle);

            Gdi32.StretchBlt(deviceContext, 0, 0, screen.Width, screen.Height,
                     display.DeviceContext, 0, 0, horizontalEnd, verticalEnd,
                     RasterOp.SRCCOPY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void blit24bpp(int[] vramBits) {
            int yRangeOffset = (240 - (displayY2 - displayY1)) >> (verticalRes == 480 ? 0 : 1);
            if (yRangeOffset < 0) yRangeOffset = 0;

            var display = new Span<int>(this.display.BitmapData.ToPointer(), 0x80000);
            Span<int> scanLine = stackalloc int[horizontalRes];

            for (int y = yRangeOffset; y < verticalRes - yRangeOffset; y++) {
                int offset = 0;
                var startXYPosition = displayVRAMXStart + ((y - yRangeOffset + displayVRAMYStart) * 1024);
                for (int x = 0; x < horizontalRes; x += 2) {
                    int p0rgb = vramBits[startXYPosition + offset++];
                    int p1rgb = vramBits[startXYPosition + offset++];
                    int p2rgb = vramBits[startXYPosition + offset++];

                    ushort p0bgr555 = GetPixelBGR555(p0rgb);
                    ushort p1bgr555 = GetPixelBGR555(p1rgb);
                    ushort p2bgr555 = GetPixelBGR555(p2rgb);

                    //[(G0R0][R1)(B0][B1G1)]
                    //   RG    B - R   GB

                    int p0R = p0bgr555 & 0xFF;
                    int p0G = (p0bgr555 >> 8) & 0xFF;
                    int p0B = p1bgr555 & 0xFF;
                    int p1R = (p1bgr555 >> 8) & 0xFF;
                    int p1G = p2bgr555 & 0xFF;
                    int p1B = (p2bgr555 >> 8) & 0xFF;

                    int p0rgb24bpp = p0R << 16 | p0G << 8 | p0B;
                    int p1rgb24bpp = p1R << 16 | p1G << 8 | p1B;

                    scanLine[x] = p0rgb24bpp;
                    scanLine[x + 1] = p1rgb24bpp;
                }
                scanLine.CopyTo(display.Slice(y * 1024));
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void blit16bpp(int[] vramBits) {
            //Console.WriteLine($"x1 {displayX1} x2 {displayX2} y1 {displayY1} y2 {displayY2}");
            //Console.WriteLine($"Display Height {display.Height}  Width {display.Width}");
            int yRangeOffset = (240 - (displayY2 - displayY1)) >> (verticalRes == 480 ? 0 : 1);
            if (yRangeOffset < 0) yRangeOffset = 0;

            var vram = new Span<int>(vramBits);
            var display = new Span<int>(this.display.BitmapData.ToPointer(), 0x80000);

            for (int y = yRangeOffset; y < verticalRes - yRangeOffset; y++) {
                var from = vram.Slice(displayVRAMXStart + ((y - yRangeOffset + displayVRAMYStart) * 1024), horizontalRes);
                var to = display.Slice(y * 1024);
                from.CopyTo(to);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort GetPixelBGR555(int color) {
            byte m = (byte)((color & 0xFF000000) >> 24);
            byte r = (byte)((color & 0x00FF0000) >> 16 + 3);
            byte g = (byte)((color & 0x0000FF00) >> 8 + 3);
            byte b = (byte)((color & 0x000000FF) >> 3);

            return (ushort)(m << 15 | b << 10 | g << 5 | r);
        }

        public int GetVPS() {
            int currentFps = fps;
            fps = 0;
            return currentFps;
        }

        public void SetDisplayMode(int horizontalRes, int verticalRes, bool is24BitDepth) {
            this.is24BitDepth = is24BitDepth;

            if (horizontalRes != this.horizontalRes || verticalRes != this.verticalRes) {
                this.horizontalRes = horizontalRes;
                this.verticalRes = verticalRes;

                clearDisplay();
                //Console.WriteLine($"setDisplayMode {horizontalRes} {verticalRes} {is24BitDepth}");
            }

        }

        public void SetVRAMStart(ushort displayVRAMXStart, ushort displayVRAMYStart) {
            this.displayVRAMXStart = displayVRAMXStart;
            this.displayVRAMYStart = displayVRAMYStart;

            //Console.WriteLine($"Vram Start {displayVRAMXStart} {displayVRAMYStart}");
        }

        public void SetVerticalRange(ushort displayY1, ushort displayY2) {
            this.displayY1 = displayY1;
            this.displayY2 = displayY2;

            //Console.WriteLine($"Vertical Range {displayY1} {displayY2}");
        }

        public void SetHorizontalRange(ushort displayX1, ushort displayX2) {
            this.displayX1 = displayX1;
            this.displayX2 = displayX2;

            //Console.WriteLine($"Horizontal Range {displayX1} {displayX2}");
        }

        private void vramViewerToggle(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.F) {
                uncapped = !uncapped;
            }
            if(e.KeyCode == Keys.Tab) {
                if (!isVramViewer) {
                    screen.Size = vramSize;
                } else {
                    screen.Size = _640x480;
                }
                isVramViewer = !isVramViewer;
                clearDisplay();
            }
        }

        private unsafe void clearDisplay() {
            Span<uint> span = new Span<uint>(display.BitmapData.ToPointer(), 0x80000);
            span.Clear();
        }

        public void Play(byte[] samples) {
            bufferedWaveProvider.AddSamples(samples, 0, samples.Length);

            // Start with a cushion, or the device's first reads come up short and
            // get padded with silence.
            if (waveOutEvent.PlaybackState != PlaybackState.Playing
                && bufferedWaveProvider.BufferedDuration.TotalMilliseconds >= AudioLowMs) {
                waveOutEvent.Play();
            }
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e) {
            Text = $"ProjectPSX | Cpu {(int)((float)cpuCyclesCounter / (PSX_MHZ / MIPS_UNDERCLOCK) * SYNC_CYCLES)}% | Vps {GetVPS()}{(uncapped ? " | FAST (F)" : "")}";
            cpuCyclesCounter = 0;
        }

        public void RunUncapped() {
            Task t = Task.Factory.StartNew(EXECUTE, TaskCreationOptions.LongRunning);
        }

        // Real-time speed: each frame takes as long as the time it emulates, so the
        // SPU makes 44100 samples a second, as fast as they play. Uncapped, the
        // emulator outran playback and the audio buffer discarded the excess
        // (crackling). The audio buffer has the last word, so the Stopwatch and
        // the sound card's clocks can't drift apart: below AudioLowMs frames run
        // without waiting, above AudioHighMs they wait for playback.
        // F toggles uncapped speed (fast-forward).
        private static readonly long FrameTicks = Stopwatch.Frequency * emulatedCyclesPerFrame / PSX_MHZ;
        private const double AudioLowMs = 60;
        private const double AudioHighMs = 150;
        private readonly Stopwatch frameClock = Stopwatch.StartNew();
        private long nextFrameTicks;
        private volatile bool uncapped;

        private void LimitSpeed() {
            long now = frameClock.ElapsedTicks;
            if (uncapped) {
                nextFrameTicks = now;
                return;
            }
            nextFrameTicks += FrameTicks;
            // Fell far behind (loading, window dragged): don't race to catch up.
            if (nextFrameTicks < now - FrameTicks * 5)
                nextFrameTicks = now;
            double audioMs = bufferedWaveProvider.BufferedDuration.TotalMilliseconds;
            if (audioMs < AudioLowMs) {
                nextFrameTicks = now;
                return;
            }
            if (audioMs > AudioHighMs)
                nextFrameTicks = Math.Max(nextFrameTicks, now + FrameTicks);
            while ((now = frameClock.ElapsedTicks) < nextFrameTicks) {
                // Sleep while there's time, spin for the last couple of ms.
                if (nextFrameTicks - now > Stopwatch.Frequency / 500)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(100);
            }
        }
        private void EXECUTE() {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            try {
                while (true) {
                    psx.RunFrame();
                    cpuCyclesCounter += cycles;
                    LimitSpeed();
                }
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
