using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SkiaSharp;

using Unite2026Seoul_MapGenCommonLib;


namespace Unite2026Seoul_MapGenAgent
{
    public sealed class MapGenAgnet
    {
        internal sealed class SaveBasicMapPngImageInput
        {
            public int LayerIndex;
            public MapActiveType[,] MapActivePlane;
            public Vector3Int FullMapSize;

            public string BasicMapGenOutputPath;

            public ManualResetEvent ManualResetEvent;
        }


#if !UNIT_TEST
        private static NamedPipeClientStream m_pipeClient;
        private static StreamReader m_reader;
        private static StreamWriter m_writer;

        private static readonly SemaphoreSlim m_writeLock = new SemaphoreSlim(1, 1);
#endif


        public static void Main(string[] args)
        {
#if !UNIT_TEST
            LinkHost();
#else
            Console.WriteLine("Unit Test Mode");
#endif

#if BENCHMARK
            Console.OutputEncoding = Encoding.UTF8;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
#endif

#if UNIT_TEST
            MapGenAgentInputBuffer.Instance.LoadData(new MapGenAgentInputData()
            {
                AbstractInput = new MapGenAgentInputData.AbstractInputData()
                {
                    MapSize = new Vector3Int()
                    {
                        x = 5,
                        y = 5,
                        z = 5
                    },
                    SingleChunkSize = new Vector3Int()
                    {
                        x = 30,
                        y = 6,
                        z = 30
                    },
                    SingleRoomSize = new Vector2Int()
                    {
                        x = 20,
                        y = 20
                    }
                },
                BasicMapInput = new MapGenAgentInputData.BasicMapInputData()
                {
                    MaxTryCountRatio = 0.001f,
                    BrushSize = new Vector2Int(1, 3),
                    MainWayFillPercent = 0.15f,
                    SubWayFillPercent = 0.25f,
                    PenetratingWayCountRate = 0.001f,
                    PenetratingWayFillPercent = 0.1f
                },
                OutputPath = new MapGenAgentInputData.OutoutPathData()
                {
                    BasicMapGenOutputPath = @"D:\Unite2026Seoul\"
                },
                Seed = 1000
            });

            Console.WriteLine("Input Data Loaded");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(MapGenAgentInputBuffer.Instance.Data));

            Console.WriteLine("\n");
#else
            byte[] bytes = System.Convert.FromBase64String(args[0]);
            string jsonString = System.Text.Encoding.UTF8.GetString(bytes);

            Console.WriteLine(jsonString);
            MapGenAgentInputBuffer.Instance.LoadData(jsonString);
#endif

            if (MapGenAgentInputBuffer.Instance.Data.Seed == 0)
            {
                MapGenAgentInputBuffer.Instance.Data = new MapGenAgentInputData()
                {
                    AbstractInput = MapGenAgentInputBuffer.Instance.Data.AbstractInput,
                    BasicMapInput = MapGenAgentInputBuffer.Instance.Data.BasicMapInput,
                    OutputPath = MapGenAgentInputBuffer.Instance.Data.OutputPath,
                    Seed = int.MaxValue
                };
            }

            #region Basic Map Gen
            BasicMapGenAgent.BasicMapGenOutput basicMapGenOutput = BasicMapGenAgent.GenerateMap();

#if BENCHMARK
            Console.WriteLine("Basic Map Generation Time: " + stopwatch.ElapsedMilliseconds / 1000.0f + "s");
#endif
            #endregion

            #region Basic Map Save
            {
                Console.WriteLine("Start Create Basic Map Png Image");

                var inputData = MapGenAgentInputBuffer.Instance.Data;
                int layerCount = inputData.AbstractInput.MapSize.y * inputData.AbstractInput.SingleChunkSize.y;

                List<ManualResetEvent> manualResetEvents = new List<ManualResetEvent>();
                int nextStartIndex = 0;
                while (true)
                {
                    bool bisEnd = false;
                    for (int index = nextStartIndex; index < layerCount; index++)
                    {
                        ManualResetEvent manualResetEvent = new ManualResetEvent(false);
                        manualResetEvents.Add(manualResetEvent);

                        Vector3Int fullMapSize = new Vector3Int()
                        {
                            x = inputData.AbstractInput.MapSize.x * inputData.AbstractInput.SingleChunkSize.x * inputData.AbstractInput.SingleRoomSize.x,
                            y = inputData.AbstractInput.MapSize.y * inputData.AbstractInput.SingleChunkSize.y,
                            z = inputData.AbstractInput.MapSize.z * inputData.AbstractInput.SingleChunkSize.z * inputData.AbstractInput.SingleRoomSize.y
                        };

                        SaveBasicMapPngImageInput input = new SaveBasicMapPngImageInput()
                        {
                            LayerIndex = index,
                            MapActivePlane = basicMapGenOutput.MapActiveCube[index],
                            FullMapSize = fullMapSize,

                            BasicMapGenOutputPath = inputData.OutputPath.BasicMapGenOutputPath,

                            ManualResetEvent = manualResetEvent
                        };

                        ThreadPool.QueueUserWorkItem(SaveBasicMapPngImage, input);

                        if (manualResetEvents.Count == 64)
                        {
                            nextStartIndex = index + 1;
                            break;
                        }
                        if (index == layerCount - 1)
                        {
                            bisEnd = true;
                        }
                    }

                    if (manualResetEvents.Count == 0)
                    {
                        break;
                    }

                    WaitHandle.WaitAll(manualResetEvents.ToArray());
                    manualResetEvents.Clear();

                    if (bisEnd)
                    {
                        break;
                    }
                }

                Console.WriteLine("End Create Basic Map Png Image");
            }

#if BENCHMARK
            Console.WriteLine("Basic Map Generation Time: " + stopwatch.ElapsedMilliseconds / 1000.0f + "s");
#endif
            #endregion

#if BENCHMARK
            stopwatch.Stop();
#endif

#if !UNIT_TEST
            _ = PostAgentFinAsync();
#endif

#if UNIT_TEST
            Console.ReadKey();
#endif
        }

        private static void SaveBasicMapPngImage(object input)
        {
            SaveBasicMapPngImageInput inputData = input as SaveBasicMapPngImageInput;

            SKBitmap rawBitmap = new SKBitmap(inputData.FullMapSize.x, inputData.FullMapSize.z);

            SKColor[] colorArray = rawBitmap.Pixels;

            for (int coord_z = 0; coord_z < inputData.FullMapSize.z; coord_z++)
            {
                for (int coord_x = 0; coord_x < inputData.FullMapSize.x; coord_x++)
                {
                    int index = (coord_z * inputData.FullMapSize.x) + coord_x;
                    var basicMapValues = DataMutiBitSaveFactors.GetTwoChannelMultiBit((int)inputData.MapActivePlane[coord_z, coord_x] / (float)byte.MaxValue);

                    //rawBitmap.SetPixel(coord_x, coord_z, new SKColor((byte)(basicMapValues.Item1 * 255.0f), (byte)(basicMapValues.Item2 * 255.0f), 255));
                    colorArray[index] = new SKColor((byte)(basicMapValues.Item1 * 255.0f), (byte)(basicMapValues.Item2 * 255.0f), 255);
                }
            }

            rawBitmap.Pixels = colorArray;

            string path = inputData.BasicMapGenOutputPath + inputData.LayerIndex.ToString() + ".png";
            using SKFileWStream fileStream = new SKFileWStream(path);
            rawBitmap.Encode(fileStream, SKEncodedImageFormat.Png, 100);

            Console.WriteLine($"Save in : {path}");

#if !UNIT_TEST
            _ = PostFinMessage2Server_BasicLayerSaveAsync(inputData.LayerIndex);
#endif

            inputData.ManualResetEvent.Set();
        }

#if !UNIT_TEST
        internal static async Task PostFinMessage2Server_BasicLayerGenAsync(int layerIndex)
        {
            if (m_writer == null || m_pipeClient == null || !m_pipeClient.IsConnected)
            {
                Console.WriteLine($"파이프 미연결로 메시지 누락: BasicLayer_{layerIndex}");
                return;
            }

            await m_writeLock.WaitAsync();

            try
            {
                await m_writer.WriteLineAsync($"BasicLayerGen_{layerIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"전송 중 에러 발생: {ex.Message}");
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        internal static async Task PostFinMessage2Server_BasicLayerSaveAsync(int layerIndex)
        {
            if (m_writer == null || m_pipeClient == null || !m_pipeClient.IsConnected)
            {
                Console.WriteLine($"파이프 미연결로 메시지 누락: BasicLayer_{layerIndex}");
                return;
            }

            await m_writeLock.WaitAsync();

            try
            {
                await m_writer.WriteLineAsync($"BasicLayerSave_{layerIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"전송 중 에러 발생: {ex.Message}");
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        internal static async Task PostAgentFinAsync()
        {
            if (m_writer == null || m_pipeClient == null || !m_pipeClient.IsConnected)
            {
                Console.WriteLine($"파이프 미연결로 메시지 누락");
                return;
            }

            await m_writeLock.WaitAsync();

            try
            {
                await m_writer.WriteLineAsync($"AgentFin");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"전송 중 에러 발생: {ex.Message}");
            }
            finally
            {
                m_writeLock.Release();
            }
        }
#endif

#if !UNIT_TEST
        private static async void LinkHost()
        {
            Console.WriteLine("Start");

            // using을 제거하고 직접 할당합니다.
            m_pipeClient = new NamedPipeClientStream(".", "UnityPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                // 서버에 연결 (타임아웃 50초 설정)
                m_pipeClient.Connect(3000);
                Console.WriteLine("Unity에 성공적으로 연결되었습니다!");

                m_reader = new StreamReader(m_pipeClient);
                // 반드시 AutoFlush = true 를 설정해야 즉각 메시지가 전송됩니다.
                m_writer = new StreamWriter(m_pipeClient) { AutoFlush = true };
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Unity 파이프 서버를 찾을 수 없습니다. (시간 초과)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"에러 발생: {ex.Message}");
            }
        }
#endif
    }
}