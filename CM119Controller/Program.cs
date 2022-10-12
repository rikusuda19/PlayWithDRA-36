using System;
using NAudio.Wave;

using System.Collections.Generic;

using HidLibrary;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

using spitalkDLL;
using System.Security.Cryptography;
// using SpeechLib;


namespace CM119Controller.CM119Controller {


    class CM119Controller {

        public HidDevice CMediaDevice;
        public Guid DirectSoundOutGuid;

        const int CM119vid = 0x0D8C;
        const int CM119pid = 0x013A;

        CancellationTokenSource cts;

        public CM119Controller() {
        }

        public CM119Controller(string CM119DevicePath, Guid DSODeviceGUID) {

            CMediaDevice = HidDevices.GetDevice(CM119DevicePath);
            DirectSoundOutGuid = DSODeviceGUID;
               
        }

        public void EnableCM119() {
            cts = new CancellationTokenSource();
            Task _ = PlaySilentSoundAsync(cts.Token);
        }

        
        public void AbortCM119() {
            cts.Cancel();
        }


        private void Main(string[] args) {

            Console.WriteLine("Hello World!");


            SapiTalk SpTalker = new SapiTalk();

            Dictionary<int, string> voiceList = SpTalker.Talkers();



            //SpTalker.Talk("こんにちは");

            //return;


            // ----------------


            CMediaDevice = GetHIDDevice(CM119vid, CM119pid);


            
            List<DirectSoundDeviceInfo> deviceList = GetDSODevices();

            Console.WriteLine("Choose your C-Media Audio Device by No.:");
            int i = 0;
            foreach(DirectSoundDeviceInfo device in deviceList) {
                Console.WriteLine("[{0:d}] {1}", i++, device.Description);
            }



            if (int.TryParse(Console.ReadLine(), out int deviceChoice) && deviceChoice >= 0 && deviceChoice <= deviceList.Count - 1) {

                DirectSoundOutGuid = deviceList[deviceChoice].Guid;

            }
            else {
                Console.WriteLine("Device Select Error");
                return;
            }

            
            Console.WriteLine("Press Enter to Transmit Saw Wave Tone; Other key to skip");

            while (Console.ReadKey(false).Key == ConsoleKey.Enter) {
                SetPTT(true);

                TransmitAudioStream(new MemoryStream(SawWaveSample()));
                 SetPTT(false);

                Console.WriteLine("Saw Wave Transmitted: Press Enter to Repeat, other key to proceed");
            }
            

            CancellationTokenSource cts = new CancellationTokenSource();
            Task _ = PlaySilentSoundAsync(cts.Token);


            Console.WriteLine("Choose Voice to Transmit, other key to quit");

            foreach (int voiceIdx in voiceList.Keys) {
                Console.WriteLine("{0:00};{1}", voiceIdx, voiceList[voiceIdx]);
    
            }
            while(int.TryParse(Console.ReadLine(), out int voiceChoice) && voiceChoice >= 0 && voiceList.ContainsKey(voiceChoice)) {

 
                    SpTalker.SetTalker(voiceChoice);
                    SpTalker.SetVolume(50);

                    // PTT on
                    SetPTT(true);

                    // 音声合成
                    MemoryStream ms = SpTalker.MakeStream("これはテストです");

                    // 選んだ音声デバイスで再生
                    ms.Position = 0;
                    TransmitAudioStream(ms, new WaveFormat(SpTalker.SpSamplesPerSecond, SpTalker.SpBitsPerSample, SpTalker.SpChannels));
                    
                    // PTT off
                    SetPTT(false);
                    Console.WriteLine("Synthesized Voice Transmitted; Choose Voice to Transmit, other key to quit");

                    foreach (int voiceIdx in voiceList.Keys) {
                        Console.WriteLine("{0:00};{1}", voiceIdx, voiceList[voiceIdx]);

                    }

            }

        }


        public bool TransmitAudioStream(Stream stream, WaveFormat format = null) {

            format ??= new WaveFormat(16000, 16, 1);

            DirectSoundOut DSOut = null, DSOutEmpty = null;
            try {

                Console.WriteLine("DirectSoundOut Created");
                DSOut = new DirectSoundOut(DirectSoundOutGuid);

                Console.WriteLine("DirectSoundOut Initialized");

                DSOut.Init(new RawSourceWaveStream(stream, format));


                Console.WriteLine("DirectSoundOut Started playing");

                
                // PTT をオンにする間の時間稼ぎ：無音のオーディオを流す
                //DSOutEmpty.Play();
                //Thread.Sleep(20);
                //SetPTT(true);
                //Thread.Sleep(100);
                //DSOutEmpty.Stop();

                // ほんちゃん
                DSOut.Play();

                while (DSOut.PlaybackState == PlaybackState.Playing) {
                    Thread.Sleep(50);  // ビープ音オフ～PTTオフまでの時間（の最大値）
                }


                Console.WriteLine("DirectSoundOut Completed playing");

                return true;
            }
            catch(Exception ex) {
                Console.Write("TX ERROR!: {0}", ex.Message);
                return false;
            }
            finally {
                DSOut?.Dispose();
                DSOutEmpty?.Dispose();
            }

        }


        private async Task PlaySilentSoundAsync(CancellationToken ct) {
            DirectSoundOut DSOutEmpty = new DirectSoundOut(DirectSoundOutGuid);
            try {
                while (true) {
                    if (ct.IsCancellationRequested) { return; }
                    DSOutEmpty.Init(new RawSourceWaveStream(new MemoryStream(EmptyWaveSample()), new WaveFormat(16000, 16, 1)));
                    await Task.Run(DSOutEmpty.Play);
                }
            }
            finally {
                cts = null;
            }
        }



        public static byte[] SawWaveSample() {
            // Wave 波形の生成
            var sampleRate = 16000;
            var frequency = 500;
            var amplitude = 0.05;
            var seconds = 2;

            var raw = new byte[sampleRate * seconds * 2];

            var multiple = 2.0 * frequency / sampleRate;
            for (int n = 0; n < sampleRate * seconds; n++) {
                var sampleSaw = ((n * multiple) % 2) - 1;
                var sampleValue = sampleSaw > 0 ? amplitude : -amplitude;
                var sample = (short)(sampleValue * Int16.MaxValue);
                var bytes = BitConverter.GetBytes(sample);
                raw[n * 2] = bytes[0];
                raw[n * 2 + 1] = bytes[1];
            }

            return raw;
        }

        
        public static byte[] EmptyWaveSample() {

            // 無音波形の生成
            var sampleRate = 16000;

            var seconds = 2;

            var raw = new byte[sampleRate * seconds * 2];

            for (int n = 0; n < sampleRate * seconds; n++) {
                raw[n * 2] = 0;
                raw[n * 2 + 1] = 0;
            }

            return raw;
        }

        
        private static List<string> GetSoundDevices() {
            List<string> deviceList = new List<string>();

            for (int i = 0; i < WaveOut.DeviceCount; i++) {
                var capabilities = WaveOut.GetCapabilities(i);
                deviceList.Add(capabilities.ProductName);
            }
            return deviceList;
        }


       public static List<DirectSoundDeviceInfo> GetDSODevices() {
            return DirectSoundOut.Devices.ToList();
        }


        public static List<HidDevice> GetHidDevices(int vid, int pid) {

            return HidDevices.Enumerate(vid, pid).ToList();

        }

        static HidDevice GetHIDDevice(int vid, int pid) {
            List<HidDevice> HidDeviceList = GetHidDevices(vid,pid);
            HidDevice HidDevice = null;

            if (HidDeviceList.Count > 0) {
                Console.WriteLine("HID Devices:");
                Console.WriteLine(HidDeviceList.Select<HidDevice, string>(x => x.ToString()).Aggregate((x, y) => y + "\r\n" + x));

                // Grab the first device
                HidDevice = HidDeviceList[0];


                // Check if connected...
                Console.WriteLine("Connected: " + HidDevice.IsConnected.ToString());
                Console.WriteLine();
            }

            return HidDevice;
        }

        public bool SetPTT(bool state) {

            byte[] OutData = new byte[CMediaDevice.Capabilities.OutputReportByteLength];

            try {
                if (state) {
                    Console.WriteLine("Send PTT ON");
                    OutData[0] = 0x00; // これがデータシートにはないが、report id として乗せなきゃいけないみたい
                    OutData[1] = 0x00; 
                    OutData[2] = 0b00000100; // drive GPIO3 to H
                    OutData[3] = 0b00000100;  // set GPIO3 to output pin
                    OutData[4] = 0x00;
                    CMediaDevice.Write(OutData);
                }
                else {
                    Console.WriteLine("Send PTT OFF");
                    OutData[0] = 0x00; // これがデータシートにはないが、report id として乗せなきゃいけないみたい
                    OutData[1] = 0x00;
                    OutData[2] = 0b00000000; // drive GPIO3 to L
                    OutData[3] = 0b00000100; // set GPIO3 to output pin
                    OutData[4] = 0x00;
                    CMediaDevice.Write(OutData);
                }

                return true;
            }
            catch (Exception ex) {
                Console.WriteLine("PTT ERROR: {0}", ex.Message);
                return false;
            }



        }
    }
}
