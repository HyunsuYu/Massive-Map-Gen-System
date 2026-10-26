using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Unite2026Seoul_MapGenCommonLib;


namespace Unite2026Seoul_MapGenAgent
{
    public enum PathDirection : int
    {
        None = 0b00000000_00000000_00000000_00000000,
        Origin = 0b00000000_00000000_00000000_00000001,
        Left = 0b00000000_00000000_00000000_00000010,
        Right = 0b00000000_00000000_00000000_00000100,
        Front = 0b00000000_00000000_00000000_00001000,
        Back = 0b00000000_00000000_00000000_00010000,
        Top = 0b00000000_00000000_00000000_00100000,
        Bottom = 0b00000000_00000000_00000000_01000000
    }


    internal static class BasicMapGenAgent
    {
        internal sealed class CalculatePenetrationgWayTaskInputData
        {
            public List<MapActiveType[,]> MapActiveTypePlanes { get; set; }

            public Vector2Int RealMapSize { get; set; }
            public Vector2Int SingleRoomSize { get; set; }
            public Vector2Int CurBlockPos { get; set; }

            public int CurLayerIndex { get; set; }

            public int PenetratingWayCount { get; set; }
            public float MaxPenetratingWayVolume { get; set; }

            public ManualResetEvent ManualResetEvent { get; set; }
        }
        public sealed class BasicMapGenOutput
        {
            private List<MapActiveType[,]> m_mapActiveCube;
            private List<PathDirection[,]> m_abstractPathDirectionCube;


            public BasicMapGenOutput()
            {
                m_mapActiveCube = new List<MapActiveType[,]>();
                m_abstractPathDirectionCube = new List<PathDirection[,]>();
            }

            public List<MapActiveType[,]> MapActiveCube
            {
                get
                {
                    return m_mapActiveCube;
                }
            }
            public List<PathDirection[,]> AbstractPathDirectionCube
            {
                get
                {
                    return m_abstractPathDirectionCube;
                }
            }
        }


        private static List<BasicMapLayerGenerator> m_basicMapLayerGenerators = new List<BasicMapLayerGenerator>();


        internal static BasicMapGenOutput GenerateMap()
        {
            var inputData = MapGenAgentInputBuffer.Instance.Data;

            #region Calculate Layer Map
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

                    BasicMapLayerGenerator.BasicMapLayerGeneratorInput input = new BasicMapLayerGenerator.BasicMapLayerGeneratorInput()
                    {
                        //MapSize = inputData.AbstractInput.MapSize,
                        MapSize = new Vector3Int()
                        {
                            x = inputData.AbstractInput.MapSize.x,
                            y = inputData.AbstractInput.MapSize.y,
                            z = inputData.AbstractInput.MapSize.z
                        },
                        SingleChunkSize = inputData.AbstractInput.SingleChunkSize,
                        SingleRoomSize = inputData.AbstractInput.SingleRoomSize,

                        MaxTryCountRatio = inputData.BasicMapInput.MaxTryCountRatio,
                        BrushSize = new Tuple<int, int>(inputData.BasicMapInput.BrushSize.x, inputData.BasicMapInput.BrushSize.y),
                        MainWayFillPercent = inputData.BasicMapInput.MainWayFillPercent,
                        SubWayFillPercent = inputData.BasicMapInput.SubWayFillPercent,

                        LayerIndex = index,

                        ManualResetEvent = manualResetEvent
                    };

                    BasicMapLayerGenerator basicMapLayerGenerator = new BasicMapLayerGenerator(input);
                    m_basicMapLayerGenerators.Add(basicMapLayerGenerator);

                    ThreadPool.QueueUserWorkItem(CalculateLayer, basicMapLayerGenerator);

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
            #endregion

            #region Calculate Penetrating Way
            List<MapActiveType[,]> mapActiveTypePlanes = new List<MapActiveType[,]>();
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                mapActiveTypePlanes.Add(m_basicMapLayerGenerators[layerIndex].BasicMapOutput.MapActiveCube);
            }
            CalculatePenetrationgWay(ref mapActiveTypePlanes,
                                     layerCount,
                                     //new Vector2Int()
                                     //{
                                     //    x = inputData.AbstractInput.MapSize.x * inputData.AbstractInput.SingleChunkSize.x,
                                     //    y = inputData.AbstractInput.MapSize.z * inputData.AbstractInput.SingleChunkSize.z
                                     //},
                                     inputData.AbstractInput.SingleRoomSize,
                                     inputData.AbstractInput.SingleRoomSize.x * inputData.AbstractInput.SingleRoomSize.y,
                                     inputData.BasicMapInput.PenetratingWayCountRate,
                                     inputData.BasicMapInput.PenetratingWayFillPercent);
            #endregion

            BasicMapGenOutput basicMapGenOutput = new BasicMapGenOutput();
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                basicMapGenOutput.MapActiveCube.Add(mapActiveTypePlanes[layerIndex]);
                basicMapGenOutput.AbstractPathDirectionCube.Add(m_basicMapLayerGenerators[layerIndex].BasicMapOutput.AbstractPathDirectionCube);
            }

            return basicMapGenOutput;
        }

        private static void CalculateLayer(object generatorObject)
        {
            BasicMapLayerGenerator basicMapLayerGenerator = generatorObject as BasicMapLayerGenerator;

            basicMapLayerGenerator.GenerateMap();
        }
        private static void CalculatePenetrationgWay(ref List<MapActiveType[,]> mapActiveTypePlanes, in int layerCount/*, in Vector2Int realMapSize*/, in Vector2Int singleRoomSize, in int singleRoomVolume, in float penetratingWayCountRate, in float penetratingWayFillPercent)
        {
            // TODO : realMapSize 수준에서 결정하지 말고, SingleRoomSize 수준에서 결정하도록 변경 -> penetratingWayCountRate를 결정할 때 보다 직관적으로 결정 가능하도록

            int maxPenetratingWayVolume = (int)(singleRoomVolume * penetratingWayFillPercent);

            for (int coord_y = 0; coord_y < layerCount - 1; coord_y++)
            {
                Vector2Int realMapSize = new Vector2Int()
                {
                    x = MapGenAgentInputBuffer.Instance.Data.AbstractInput.MapSize.x * MapGenAgentInputBuffer.Instance.Data.AbstractInput.SingleChunkSize.x,
                    y = MapGenAgentInputBuffer.Instance.Data.AbstractInput.MapSize.z * MapGenAgentInputBuffer.Instance.Data.AbstractInput.SingleChunkSize.z
                };
                long singleLayerMapVolume = realMapSize.x * realMapSize.y;
                int penetratingWayCount = (int)(singleLayerMapVolume * penetratingWayCountRate);
                if (penetratingWayCount == 0)
                {
                    penetratingWayCount = 1;
                }

                List<ManualResetEvent> manualResetEvents = new List<ManualResetEvent>();
                int nextStartIndex = 0;
                while (true)
                {
                    bool bisEnd = false;
                    for (int index = nextStartIndex; index < singleRoomVolume; index++)
                    {
                        ManualResetEvent manualResetEvent = new ManualResetEvent(false);
                        manualResetEvents.Add(manualResetEvent);

                        CalculatePenetrationgWayTaskInputData inputData = new CalculatePenetrationgWayTaskInputData()
                        {
                            MapActiveTypePlanes = mapActiveTypePlanes,
                            RealMapSize = realMapSize,
                            SingleRoomSize = singleRoomSize,
                            CurBlockPos = new Vector2Int(index % singleRoomSize.x, index / singleRoomSize.x),
                            CurLayerIndex = coord_y,
                            PenetratingWayCount = penetratingWayCount,
                            MaxPenetratingWayVolume = maxPenetratingWayVolume,
                            ManualResetEvent = manualResetEvent
                        };

                        ThreadPool.QueueUserWorkItem(CalculatePenetrationgWayTask, inputData);

                        if (manualResetEvents.Count == 64)
                        {
                            nextStartIndex = index + 1;
                            break;
                        }
                        if (index == singleRoomVolume - 1)
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
            }
        }

        private static void CalculatePenetrationgWayTask(object input)
        {
            CalculatePenetrationgWayTaskInputData inputData = input as CalculatePenetrationgWayTaskInputData;

            FastRandom random = new FastRandom((uint)(MapGenAgentInputBuffer.Instance.Data.Seed + inputData.CurLayerIndex));

            List<Vector2Int> penetratingWayTryPosCandidates = new List<Vector2Int>();
            Vector2Int startPos = new Vector2Int()
            {
                x = inputData.RealMapSize.x * inputData.CurBlockPos.x,
                y = inputData.RealMapSize.y * inputData.CurBlockPos.y
            };
            Vector2Int endPos = new Vector2Int()
            {
                x = inputData.RealMapSize.x * (inputData.CurBlockPos.x + 1),
                y = inputData.RealMapSize.y * (inputData.CurBlockPos.y + 1)
            };
            for (int coord_z = startPos.y; coord_z < endPos.y; coord_z++)
            {
                for (int coord_x = startPos.x; coord_x < endPos.x; coord_x++)
                {
                    if (inputData.MapActiveTypePlanes[inputData.CurLayerIndex][coord_z, coord_x].HasFlag(MapActiveType.BIsNodeActive) &&
                        inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][coord_z, coord_x].HasFlag(MapActiveType.BIsNodeActive))
                    {
                        penetratingWayTryPosCandidates.Add(new Vector2Int()
                        {
                            x = coord_x,
                            y = coord_z
                        });
                    }
                }
            }

            int curPenetratingWayCount = inputData.PenetratingWayCount;
            if (RandUtil.GetHalfChance())
            {
                curPenetratingWayCount -= (int)(inputData.PenetratingWayCount * 0.1f);
            }
            else
            {
                curPenetratingWayCount += (int)(inputData.PenetratingWayCount * 0.1f);
            }

            for (int count = 0; count < curPenetratingWayCount; count++)
            {
                if (penetratingWayTryPosCandidates.Count == 0)
                {
                    break;
                }

                List<Vector2Int> nextSetTryTargetPoses = new List<Vector2Int>();

                nextSetTryTargetPoses.Add(penetratingWayTryPosCandidates[random.Next(0, penetratingWayTryPosCandidates.Count)]);
                penetratingWayTryPosCandidates.Remove(nextSetTryTargetPoses[0]);

                int curMaxPenetratingWayVolume = (int)(inputData.MaxPenetratingWayVolume * (((float)random.NextDouble() / 2.0f) + 0.5f));
                int curSettlePenetratingWayVolume = 0;
                while (true)
                {
                    bool bisEnd = false;

                    if (nextSetTryTargetPoses.Count == 0)
                    {
                        bisEnd = true;
                    }

                    List<Vector2Int> nextSetTryTargetPosesBuffer = new List<Vector2Int>();
                    foreach (Vector2Int nextSetTryTargetPos in nextSetTryTargetPoses)
                    {
                        if (curSettlePenetratingWayVolume + 1 > inputData.MaxPenetratingWayVolume)
                        {
                            bisEnd = true;
                            break;
                        }

                        inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextSetTryTargetPos.y, nextSetTryTargetPos.x] |= MapActiveType.BIsNodeIsGateToBack;

                        curSettlePenetratingWayVolume++;

                        Vector2Int nextPathDirectionSetTryTargetPos = new Vector2Int(nextSetTryTargetPos.x, nextSetTryTargetPos.y - 1);
                        if (nextPathDirectionSetTryTargetPos.y >= startPos.y &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack))
                        {
                            nextSetTryTargetPosesBuffer.Add(nextPathDirectionSetTryTargetPos);
                            if (penetratingWayTryPosCandidates.Contains(nextPathDirectionSetTryTargetPos))
                            {
                                penetratingWayTryPosCandidates.Remove(nextPathDirectionSetTryTargetPos);
                            }
                        }

                        nextPathDirectionSetTryTargetPos = new Vector2Int(nextSetTryTargetPos.x, nextSetTryTargetPos.y + 1);
                        if (nextPathDirectionSetTryTargetPos.y < endPos.y &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack))
                        {
                            nextSetTryTargetPosesBuffer.Add(nextPathDirectionSetTryTargetPos);
                            if (penetratingWayTryPosCandidates.Contains(nextPathDirectionSetTryTargetPos))
                            {
                                penetratingWayTryPosCandidates.Remove(nextPathDirectionSetTryTargetPos);
                            }
                        }

                        nextPathDirectionSetTryTargetPos = new Vector2Int(nextSetTryTargetPos.x - 1, nextSetTryTargetPos.y);
                        if (nextPathDirectionSetTryTargetPos.x >= startPos.x &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack))
                        {
                            nextSetTryTargetPosesBuffer.Add(nextPathDirectionSetTryTargetPos);
                            if (penetratingWayTryPosCandidates.Contains(nextPathDirectionSetTryTargetPos))
                            {
                                penetratingWayTryPosCandidates.Remove(nextPathDirectionSetTryTargetPos);
                            }
                        }

                        nextPathDirectionSetTryTargetPos = new Vector2Int(nextSetTryTargetPos.x + 1, nextSetTryTargetPos.y);
                        if (nextPathDirectionSetTryTargetPos.x < endPos.x &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeActive) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack) &&
                            !inputData.MapActiveTypePlanes[inputData.CurLayerIndex + 1][nextPathDirectionSetTryTargetPos.y, nextPathDirectionSetTryTargetPos.x].HasFlag(MapActiveType.BIsNodeIsGateToBack))
                        {
                            nextSetTryTargetPosesBuffer.Add(nextPathDirectionSetTryTargetPos);
                            if (penetratingWayTryPosCandidates.Contains(nextPathDirectionSetTryTargetPos))
                            {
                                penetratingWayTryPosCandidates.Remove(nextPathDirectionSetTryTargetPos);
                            }
                        }
                    }
                    nextSetTryTargetPoses.Clear();
                    nextSetTryTargetPoses = nextSetTryTargetPosesBuffer;

                    if (bisEnd)
                    {
                        break;
                    }
                }
            }

            random = null;

            inputData.ManualResetEvent.Set();
        }
    }

    internal sealed class BasicMapLayerGenerator : IDisposable
    {
        private struct DetailNode
        {
            public PathDirection[,] DivisionWayDirectionTable;
            public CoordPair CoordPair;
            public MapActiveType[,] BlockActiveTable;


            public DetailNode(Vector2Int m_singleRoomSize)
            {
                DivisionWayDirectionTable = new PathDirection[m_singleRoomSize.y, m_singleRoomSize.x];
                BlockActiveTable = new MapActiveType[m_singleRoomSize.y, m_singleRoomSize.x];

                CoordPair = new CoordPair();
            }

            public void Dispose()
            {
                DivisionWayDirectionTable = null;
                BlockActiveTable = null;
            }
        }
        private struct CoordPair
        {
            public Vector2Int StartCoord, EndCoord;


            public CoordPair()
            {
                StartCoord = new Vector2Int();
                EndCoord = new Vector2Int();
            }
        }

        public sealed class BasicMapLayerGeneratorOutput : IDisposable
        {
            public MapActiveType[,] MapActiveCube;

            public PathDirection[,] AbstractPathDirectionCube;


            ~BasicMapLayerGeneratorOutput()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
            }

            private void Dispose(in bool bisDispose)
            {
                if (bisDispose)
                {
                    MapActiveCube = null;

                    AbstractPathDirectionCube = null;
                }
            }
        }

        public sealed class BasicMapLayerGeneratorInput
        {
            public required Vector3Int MapSize { get; set; }
            public required Vector3Int SingleChunkSize { get; set; }
            public required Vector2Int SingleRoomSize { get; set; }

            public required float MaxTryCountRatio { get; set; }
            public required Tuple<int, int> BrushSize { get; set; }
            public required float MainWayFillPercent { get; set; }
            public required float SubWayFillPercent { get; set; }

            public required int LayerIndex { get; set; }

            public required ManualResetEvent ManualResetEvent { get; set; }
        }

        private sealed class DetailCalculateInputData
        {
            public required CoordPair CoordPair { get; set; }
            public required ManualResetEvent ManualResetEvent { get; set; }
        }


        private PathDirection[,] m_abstractPlane;
        private DetailNode[,] m_detailPlane;

        private int[,] m_abstractRegionPlane;

        private List<CoordPair> m_abstractCoordPairs;

        private Vector3Int m_realMapSize;
        private Vector2Int m_singleRoomSize;

        private int m_maxTryCount, m_brushMinSize, m_brushMaxSize;
        private float m_mainWayFillPercent, m_subWayFillPercent;

        //private FastRandom random = new FastRandom((uint)MapGenAgentInputBuffer.Instance.Data.Seed);

        private ManualResetEvent m_manualResetEvent;
        private BasicMapLayerGeneratorOutput m_basicLayerMapGeneratorOutput;

        private int m_layerIndex;


        public BasicMapLayerGenerator(in BasicMapLayerGeneratorInput mapGenInputData)
        {
            Vector3Int mapSize = mapGenInputData.MapSize;
            Vector3Int singleChunkSize = mapGenInputData.SingleChunkSize;

            m_realMapSize = new Vector3Int(mapSize.x * singleChunkSize.x, mapSize.y * singleChunkSize.y, mapSize.z * singleChunkSize.z);
            m_singleRoomSize = mapGenInputData.SingleRoomSize;

            m_abstractPlane = new PathDirection[m_realMapSize.z, m_realMapSize.x];
            m_abstractCoordPairs = new List<CoordPair>();
            m_detailPlane = new DetailNode[m_realMapSize.z, m_realMapSize.x];
            for (int coord_z = 0; coord_z < m_realMapSize.z; coord_z++)
            {
                for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                {
                    m_detailPlane[coord_z, coord_x] = new DetailNode(m_singleRoomSize);
                }
            }

            m_abstractRegionPlane = new int[m_realMapSize.z, m_realMapSize.x];

            m_maxTryCount = (int)((m_realMapSize.x * m_realMapSize.z) * mapGenInputData.MaxTryCountRatio);
            m_maxTryCount = m_maxTryCount == 0 ? 1 : m_maxTryCount;

            var brushSIze = mapGenInputData.BrushSize;
            m_brushMinSize = brushSIze.Item1;
            m_brushMaxSize = brushSIze.Item2;

            m_mainWayFillPercent = mapGenInputData.MainWayFillPercent;
            m_subWayFillPercent = mapGenInputData.SubWayFillPercent;

            m_layerIndex = mapGenInputData.LayerIndex;

            m_manualResetEvent = mapGenInputData.ManualResetEvent;
        }
        ~BasicMapLayerGenerator()
        {
            Dispose(false);
        }

        public BasicMapLayerGeneratorOutput BasicMapOutput
        {
            get
            {
                return m_basicLayerMapGeneratorOutput;
            }
        }

        public void GenerateMap()
        {
            CalculateAbstractPlane();

            CalculateDetailPlane();

            MapActiveType[,] mapActiveCube = new MapActiveType[m_realMapSize.z * m_singleRoomSize.y, m_realMapSize.x * m_singleRoomSize.x];

            for (int coord_y = 0; coord_y < m_realMapSize.z; coord_y++)
            {
                for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                {
                    for (int detailCoord_y = 0; detailCoord_y < m_singleRoomSize.y; detailCoord_y++)
                    {
                        for (int detailCoord_x = 0; detailCoord_x < m_singleRoomSize.x; detailCoord_x++)
                        {
                            mapActiveCube[coord_y * m_singleRoomSize.y + detailCoord_y, coord_x * m_singleRoomSize.x + detailCoord_x] = m_detailPlane[coord_y, coord_x].BlockActiveTable[detailCoord_y, detailCoord_x];
                            //mapActiveCube[coord_y * m_singleRoomSize.y + detailCoord_y, coord_x * m_singleRoomSize.x + detailCoord_x] |= MapActiveType.BIsNodeMiddleLayerGenerationEnable;
                        }
                    }
                }
            }

            m_basicLayerMapGeneratorOutput = new BasicMapLayerGeneratorOutput()
            {
                MapActiveCube = mapActiveCube,
                AbstractPathDirectionCube = (PathDirection[,])m_abstractPlane.Clone()
            };

            m_manualResetEvent.Set();
            Console.WriteLine("End Basic Layer - " + m_layerIndex);
#if !UNIT_TEST
            _ = MapGenAgnet.PostFinMessage2Server_BasicLayerGenAsync(m_layerIndex);
#endif
        }

        #region Calculate Abstract Plane
        private void CalculateAbstractPlane()
        {
            FastRandom random = new FastRandom((uint)(MapGenAgentInputBuffer.Instance.Data.Seed + m_layerIndex));

            int totalAccessibleCount = m_realMapSize.x * m_realMapSize.z;

            float curFillPercent = 0.0f, abstractUnitValue = 1.0f / totalAccessibleCount;
            Vector2Int StartCoord = default(Vector2Int), EndCoord = default(Vector2Int);
            int tryCount = 0;

            bool bisFirstItr = true;
            bool startFlag = false, endFlag = false;
            while (true)
            {
                if (curFillPercent >= m_mainWayFillPercent)
                {
                    break;
                }
                if (tryCount >= m_maxTryCount)
                {
                    break;
                }

                if (bisFirstItr)
                {
                    for (int coord_z = 0; coord_z < m_realMapSize.z; coord_z++)
                    {
                        for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                        {
                            m_abstractRegionPlane[coord_z, coord_x] = 1;
                        }
                    }

                    if (!startFlag)
                    {
                        while (true)
                        {
                            StartCoord.x = m_realMapSize.x / 2;
                            StartCoord.y = m_realMapSize.z - 1;

                            if ((m_abstractPlane[StartCoord.y, StartCoord.x] ^ PathDirection.None) == PathDirection.None)
                            {
                                startFlag = true;
                                break;
                            }
                        }
                    }
                    if (!endFlag)
                    {
                        while (true)
                        {
                            EndCoord.x = random.Next(0, m_realMapSize.x);
                            EndCoord.y = 0;

                            if ((m_abstractPlane[EndCoord.y, EndCoord.x] ^ PathDirection.None) == PathDirection.None)
                            {
                                endFlag = true;
                                break;
                            }
                        }
                    }

                    bisFirstItr = false;
                }
                else
                {
                    for (int coord_z = 0; coord_z < m_realMapSize.z; coord_z++)
                    {
                        for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                        {
                            m_abstractRegionPlane[coord_z, coord_x] = -1;
                        }
                    }

                    int curFalgNum = 1;
                    for (int coord_z = 0; coord_z < m_realMapSize.z; coord_z++)
                    {
                        for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                        {
                            if (coord_z - 1 >= 0 && m_abstractRegionPlane[coord_z - 1, coord_x] != -1)
                            {
                                m_abstractRegionPlane[coord_z, coord_x] = m_abstractRegionPlane[coord_z - 1, coord_x];
                            }
                            else if (coord_z + 1 < m_realMapSize.z && m_abstractRegionPlane[coord_z + 1, coord_x] != -1)
                            {
                                m_abstractRegionPlane[coord_z, coord_x] = m_abstractRegionPlane[coord_z + 1, coord_x];
                            }
                            else if (coord_x - 1 >= 0 && m_abstractRegionPlane[coord_z, coord_x - 1] != -1)
                            {
                                m_abstractRegionPlane[coord_z, coord_x] = m_abstractRegionPlane[coord_z, coord_x - 1];
                            }
                            else if (coord_x + 1 < m_realMapSize.x && m_abstractRegionPlane[coord_z, coord_x + 1] != -1)
                            {
                                m_abstractRegionPlane[coord_z, coord_x] = m_abstractRegionPlane[coord_z, coord_x + 1];
                            }
                            else
                            {
                                m_abstractRegionPlane[coord_z, coord_x] = curFalgNum;
                                curFalgNum++;
                            }
                        }
                    }

                    if (!startFlag)
                    {
                        while (true)
                        {
                            StartCoord.x = random.Next(0, m_realMapSize.x);
                            StartCoord.y = random.Next(0, m_realMapSize.z);

                            if ((m_abstractPlane[StartCoord.y, StartCoord.x] ^ PathDirection.None) == PathDirection.None)
                            {
                                startFlag = true;
                                break;
                            }
                        }
                    }
                    if (!endFlag)
                    {
                        while (true)
                        {
                            EndCoord.x = random.Next(0, m_realMapSize.x);
                            EndCoord.y = random.Next(0, m_realMapSize.z);

                            if ((m_abstractPlane[EndCoord.y, EndCoord.x] ^ PathDirection.None) == PathDirection.None)
                            {
                                endFlag = true;
                                break;
                            }
                        }
                    }

                    if (StartCoord == EndCoord)
                    {
                        startFlag = false;
                        endFlag = false;

                        tryCount++;
                        continue;
                    }
                    else if (m_abstractRegionPlane[StartCoord.y, StartCoord.x] != m_abstractRegionPlane[EndCoord.y, EndCoord.x])
                    {
                        startFlag = false;
                        endFlag = false;

                        tryCount++;
                        continue;
                    }
                }

                CalculateSingleAbstractDirectionTable(StartCoord, EndCoord);
                startFlag = false;
                endFlag = false;

                //UpdateGlobalDistributionPlane();

                int fillCount = 0;
                for (int coord_y = 0; coord_y < m_realMapSize.z; coord_y++)
                {
                    for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                    {
                        if ((m_abstractPlane[coord_y, coord_x] ^ PathDirection.None) == PathDirection.None)
                        {
                            fillCount++;
                        }
                    }
                }
                curFillPercent = 1.0f - fillCount * abstractUnitValue;
            }

            if (m_mainWayFillPercent != 0.0f)
            {
                int fillSingleRoomTargetCount = (int)(totalAccessibleCount * m_subWayFillPercent), fillSingleRoomCount = 0;
                Vector2Int tempCoord = new Vector2Int()
                {
                    x = 0,
                    y = 0
                };
                while (true)
                {
                    tempCoord.x = random.Next(0, m_realMapSize.x);
                    tempCoord.y = random.Next(0, m_realMapSize.z);

                    if ((m_abstractPlane[tempCoord.y, tempCoord.x] ^ PathDirection.None) == PathDirection.None)
                    {
                        List<PathDirection> validDirections = new List<PathDirection>();
                        if (tempCoord.y - 1 >= 0 && !((m_abstractPlane[tempCoord.y - 1, tempCoord.x] ^ PathDirection.None) == PathDirection.None))
                        {
                            validDirections.Add(PathDirection.Top);
                        }
                        if (tempCoord.y + 1 < m_realMapSize.z && !((m_abstractPlane[tempCoord.y + 1, tempCoord.x] ^ PathDirection.None) == PathDirection.None))
                        {
                            validDirections.Add(PathDirection.Bottom);
                        }
                        if (tempCoord.x - 1 >= 0 && !((m_abstractPlane[tempCoord.y, tempCoord.x - 1] ^ PathDirection.None) == PathDirection.None))
                        {
                            validDirections.Add(PathDirection.Left);
                        }
                        if (tempCoord.x + 1 < m_realMapSize.x && !((m_abstractPlane[tempCoord.y, tempCoord.x + 1] ^ PathDirection.None) == PathDirection.None))
                        {
                            validDirections.Add(PathDirection.Right);
                        }

                        if (validDirections.Count == 0)
                        {
                            continue;
                        }

                        m_abstractPlane[tempCoord.y, tempCoord.x] = validDirections[random.Next(0, validDirections.Count)];

                        m_abstractCoordPairs.Add(new CoordPair()
                        {
                            StartCoord = tempCoord,
                            EndCoord = tempCoord
                        });

                        fillSingleRoomCount++;

                        //UpdateGlobalDistributionPlane();
                    }

                    if (fillSingleRoomCount == fillSingleRoomTargetCount)
                    {
                        break;
                    }
                }
            }
        }

        private void CalculateSingleAbstractDirectionTable(Vector2Int StartCoord, Vector2Int EndCoord)
        {
            FastRandom random = new FastRandom((uint)(MapGenAgentInputBuffer.Instance.Data.Seed + StartCoord.x + StartCoord.y + EndCoord.x + EndCoord.y));

            PathDirection[,] backupTable = new PathDirection[m_realMapSize.z, m_realMapSize.x];
            for (int coord_y = 0; coord_y < m_realMapSize.z; coord_y++)
            {
                for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                {
                    backupTable[coord_y, coord_x] = m_abstractPlane[coord_y, coord_x];
                }
            }

            Vector2Int curCoord = StartCoord;
            Vector4 distance = default(Vector4), finalChance = default(Vector4), failCondition = new Vector4()
            {
                x = -1.0f,
                y = -1.0f,
                z = -1.0f,
                w = -1.0f
            };
            int curTryCount = 0;
            bool clear = true;
            while (true)
            {
                //  Calculate distance
                distance.x = (curCoord.y - 1 >= 0 && (m_abstractPlane[curCoord.y - 1, curCoord.x] ^ PathDirection.None) == PathDirection.None) ? GetDistance(curCoord, EndCoord) : -1.0f;
                distance.y = (curCoord.y + 1 <= m_realMapSize.z - 1 && (m_abstractPlane[curCoord.y + 1, curCoord.x] ^ PathDirection.None) == PathDirection.None) ? GetDistance(curCoord, EndCoord) : -1.0f;
                distance.z = (curCoord.x - 1 >= 0 && (m_abstractPlane[curCoord.y, curCoord.x - 1] ^ PathDirection.None) == PathDirection.None) ? GetDistance(curCoord, EndCoord) : -1.0f;
                distance.w = (curCoord.x + 1 <= m_realMapSize.x - 1 && (m_abstractPlane[curCoord.y, curCoord.x + 1] ^ PathDirection.None) == PathDirection.None) ? GetDistance(curCoord, EndCoord) : -1.0f;


                if (distance == failCondition)
                {
                    for (int coord_y = 0; coord_y < m_realMapSize.z; coord_y++)
                    {
                        for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                        {
                            m_abstractPlane[coord_y, coord_x] = backupTable[coord_y, coord_x];
                        }
                    }

                    curCoord = StartCoord;

                    curTryCount++;
                }
                if (curTryCount >= m_maxTryCount)
                {
                    clear = false;
                    break;
                }

                float totalAvailableDistance = default(float);

                if (distance.x != -1.0f)
                {
                    totalAvailableDistance += distance.x;
                }
                if (distance.y != -1.0f)
                {
                    totalAvailableDistance += distance.y;
                }
                if (distance.z != -1.0f)
                {
                    totalAvailableDistance += distance.z;
                }
                if (distance.w != -1.0f)
                {
                    totalAvailableDistance += distance.w;
                }

                finalChance.x = (distance.x != -1.0f) ? distance.x / totalAvailableDistance : 0.0f;
                finalChance.y = (distance.y != -1.0f) ? distance.y / totalAvailableDistance : 0.0f;
                finalChance.z = (distance.z != -1.0f) ? distance.z / totalAvailableDistance : 0.0f;
                finalChance.w = (distance.w != -1.0f) ? distance.w / totalAvailableDistance : 0.0f;

                float randomFloat = (float)random.NextDouble();

                if (curCoord == EndCoord)
                {
                    if (randomFloat < finalChance.x)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Top;
                    }
                    else if (randomFloat < finalChance.x + finalChance.y)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Bottom;
                    }
                    else if (randomFloat < finalChance.x + finalChance.y + finalChance.z)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Left;
                    }
                    else if (randomFloat < finalChance.x + finalChance.y + finalChance.z + finalChance.w)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Right;
                    }

                    break;
                }
                else
                {
                    if (randomFloat < finalChance.x)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Top;
                        curCoord.y -= 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Bottom;
                        curCoord.y += 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y + finalChance.z)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Left;
                        curCoord.x -= 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y + finalChance.z + finalChance.w)
                    {
                        m_abstractPlane[curCoord.y, curCoord.x] = PathDirection.Right;
                        curCoord.x += 1;
                        continue;
                    }
                }
            }

            backupTable = null;

            if (clear)
            {
                m_abstractCoordPairs.Add(new CoordPair()
                {
                    StartCoord = StartCoord,
                    EndCoord = EndCoord
                });
            }
        }
        #endregion

        #region Calculate Detail Plane
        private void CalculateDetailPlane()
        {
            if (m_abstractCoordPairs.Count == 0)
            {
                return;
            }

            List<ManualResetEvent> manualResetEvents = new List<ManualResetEvent>();
            int nextStartIndex = 0;

            //  Direction table
            while (true)
            {
                bool bisEnd = false;
                for (int index = nextStartIndex; index < m_abstractCoordPairs.Count; index++)
                {
                    ManualResetEvent manualResetEvent = new ManualResetEvent(false);
                    manualResetEvents.Add(manualResetEvent);

                    DetailCalculateInputData inputData = new DetailCalculateInputData()
                    {
                        CoordPair = m_abstractCoordPairs[index],
                        ManualResetEvent = manualResetEvent
                    };

                    ThreadPool.QueueUserWorkItem(CalculateSingleDetailNodeTable, inputData);

                    if (manualResetEvents.Count == 64)
                    {
                        nextStartIndex = index + 1;
                        break;
                    }
                    if (index == m_abstractCoordPairs.Count - 1)
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

            //  Block active table
            manualResetEvents.Clear();
            nextStartIndex = 0;
            while (true)
            {
                bool bisEnd = false;
                for (int index = nextStartIndex; index < m_abstractCoordPairs.Count; index++)
                {
                    ManualResetEvent manualResetEvent = new ManualResetEvent(false);
                    manualResetEvents.Add(manualResetEvent);

                    DetailCalculateInputData inputData = new DetailCalculateInputData()
                    {
                        CoordPair = m_abstractCoordPairs[index],
                        ManualResetEvent = manualResetEvent
                    };

                    ThreadPool.QueueUserWorkItem(CalculateSingeDetailNodeBlockActiveTable, inputData);

                    if (manualResetEvents.Count == 64)
                    {
                        nextStartIndex = index + 1;
                        break;
                    }
                    if (index == m_abstractCoordPairs.Count - 1)
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
        }

        private void CalculateSingleDetailNodeTable(object inputData)
        {
            DetailCalculateInputData input = inputData as DetailCalculateInputData;
            CoordPair CoordPair = input.CoordPair;

            FastRandom random = new FastRandom((uint)(MapGenAgentInputBuffer.Instance.Data.Seed + input.CoordPair.StartCoord.x + input.CoordPair.StartCoord.y + input.CoordPair.EndCoord.x + input.CoordPair.EndCoord.y));

            //  Calculate  DivisionWayDirectionTable
            Vector2Int abstractCurCoord = CoordPair.StartCoord;
            Vector2Int detailStartCoord = default(Vector2Int), detailEndCoord = default(Vector2Int);
            while (true)
            {
                //  Set detail start coord
                if (abstractCurCoord == CoordPair.StartCoord)
                {
                    detailStartCoord.x = random.Next(1, m_singleRoomSize.x - 1);
                    detailStartCoord.y = 0;

                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord.x = detailStartCoord.x;
                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord.y = detailStartCoord.y;
                }

                //  Set detail end coord
                if (abstractCurCoord == CoordPair.EndCoord)
                {
                    detailEndCoord.x = random.Next(1, m_singleRoomSize.x - 1);
                    detailEndCoord.y = m_singleRoomSize.y - 1;

                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.EndCoord.x = detailEndCoord.x;
                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.EndCoord.y = detailEndCoord.y;
                }
                else
                {
                    switch (m_abstractPlane[abstractCurCoord.y, abstractCurCoord.x])
                    {
                        case PathDirection.Top:
                            detailEndCoord.x = random.Next(1, m_singleRoomSize.x - 1);
                            detailEndCoord.y = 0;
                            break;

                        case PathDirection.Bottom:
                            detailEndCoord.x = random.Next(1, m_singleRoomSize.x - 1);
                            detailEndCoord.y = m_singleRoomSize.y - 1;
                            break;

                        case PathDirection.Left:
                            detailEndCoord.x = 0;
                            detailEndCoord.y = random.Next(1, m_singleRoomSize.y - 1);
                            break;


                        case PathDirection.Right:
                            detailEndCoord.x = m_singleRoomSize.x - 1;
                            detailEndCoord.y = random.Next(1, m_singleRoomSize.y - 1);
                            break;
                    }

                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.EndCoord.x = detailEndCoord.x;
                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.EndCoord.y = detailEndCoord.y;
                }


                Vector2Int detailCurCoord = detailStartCoord;
                Vector4 distance = default(Vector4), finalChance = default(Vector4), failCondition = new Vector4()
                {
                    x = -1.0f,
                    y = -1.0f,
                    z = -1.0f,
                    w = -1.0f
                };
                while (detailCurCoord != detailEndCoord)
                {
                    //  Calculate distance
                    distance.x = (detailCurCoord.y - 1 >= 0 && (m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y - 1, detailCurCoord.x] ^ PathDirection.None) == PathDirection.None) ? GetDistance(detailCurCoord, detailEndCoord) : -1.0f;
                    distance.y = (detailCurCoord.y + 1 <= m_singleRoomSize.y - 1 && (m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y + 1, detailCurCoord.x] ^ PathDirection.None) == PathDirection.None) ? GetDistance(detailCurCoord, detailEndCoord) : -1.0f;
                    distance.z = (detailCurCoord.x - 1 >= 0 && (m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x - 1] ^ PathDirection.None) == PathDirection.None) ? GetDistance(detailCurCoord, detailEndCoord) : -1.0f;
                    distance.w = (detailCurCoord.x + 1 <= m_singleRoomSize.x - 1 && (m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x + 1] ^ PathDirection.None) == PathDirection.None) ? GetDistance(detailCurCoord, detailEndCoord) : -1.0f;

                    //  Calculate total distnaces and check is it corner
                    float totalAvailableDistance = default(float);

                    if (distance.x != -1.0f)
                    {
                        if ((detailCurCoord.y - 1 == 0 && detailCurCoord.x == 0) || (detailCurCoord.y - 1 == 0 && detailCurCoord.x == m_singleRoomSize.x - 1))
                        {
                            distance.x = -1.0f;
                        }
                        else
                        {
                            totalAvailableDistance += distance.x;
                        }
                    }
                    if (distance.y != -1.0f)
                    {
                        if ((detailCurCoord.y + 1 == m_singleRoomSize.y - 1 && detailCurCoord.x == 0) || (detailCurCoord.y + 1 == m_singleRoomSize.y - 1 && detailCurCoord.x == m_singleRoomSize.x - 1))
                        {
                            distance.y = -1.0f;
                        }
                        else
                        {
                            totalAvailableDistance += distance.y;
                        }
                    }
                    if (distance.z != -1.0f)
                    {
                        if ((detailCurCoord.y == 0 && detailCurCoord.x - 1 == 0) || (detailCurCoord.y == m_singleRoomSize.y - 1 && detailCurCoord.x - 1 == 0))
                        {
                            distance.z = -1.0f;
                        }
                        else
                        {
                            totalAvailableDistance += distance.z;
                        }
                    }
                    if (distance.w != -1.0f)
                    {
                        if ((detailCurCoord.y == 0 && detailCurCoord.x + 1 == m_singleRoomSize.x - 1) || (detailCurCoord.y == m_singleRoomSize.y - 1 && detailCurCoord.x + 1 == m_singleRoomSize.x - 1))
                        {
                            distance.w = -1.0f;
                        }
                        else
                        {
                            totalAvailableDistance += distance.w;
                        }
                    }

                    //  Fail check
                    if (distance == failCondition)
                    {
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable = new PathDirection[m_singleRoomSize.y, m_singleRoomSize.x];
                        detailCurCoord = detailStartCoord;
                    }

                    finalChance.x = (distance.x != -1.0f) ? distance.x / totalAvailableDistance : 0.0f;
                    finalChance.y = (distance.y != -1.0f) ? distance.y / totalAvailableDistance : 0.0f;
                    finalChance.z = (distance.z != -1.0f) ? distance.z / totalAvailableDistance : 0.0f;
                    finalChance.w = (distance.w != -1.0f) ? distance.w / totalAvailableDistance : 0.0f;

                    float randomFloat = (float)random.NextDouble();

                    if (randomFloat < finalChance.x)
                    {
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x] = PathDirection.Top;
                        detailCurCoord.y -= 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y)
                    {
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x] = PathDirection.Bottom;
                        detailCurCoord.y += 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y + finalChance.z)
                    {
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x] = PathDirection.Left;
                        detailCurCoord.x -= 1;
                        continue;
                    }
                    if (randomFloat < finalChance.x + finalChance.y + finalChance.z + finalChance.w)
                    {
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x] = PathDirection.Right;
                        detailCurCoord.x += 1;
                        continue;
                    }
                }

                switch (m_abstractPlane[abstractCurCoord.y, abstractCurCoord.x])
                {
                    case PathDirection.Top:
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailEndCoord.y, detailEndCoord.x] = PathDirection.Top;
                        break;

                    case PathDirection.Bottom:
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailEndCoord.y, detailEndCoord.x] = PathDirection.Bottom;
                        break;

                    case PathDirection.Left:
                        m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailEndCoord.y, detailEndCoord.x] = PathDirection.Left;
                        break;

                    case PathDirection.Right:
                        m_detailPlane
                            [abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailEndCoord.y, detailEndCoord.x] = PathDirection.Right;
                        break;
                }


                if (abstractCurCoord == CoordPair.EndCoord)
                {
                    input.ManualResetEvent.Set();
                    return;
                }

                switch (m_abstractPlane[abstractCurCoord.y, abstractCurCoord.x])
                {
                    case PathDirection.Top:
                        abstractCurCoord.y -= 1;

                        detailStartCoord.x = detailEndCoord.x;
                        detailStartCoord.y = m_singleRoomSize.y - 1;
                        break;

                    case PathDirection.Bottom:
                        abstractCurCoord.y += 1;

                        detailStartCoord.x = detailEndCoord.x;
                        detailStartCoord.y = 0;
                        break;

                    case PathDirection.Left:
                        abstractCurCoord.x -= 1;

                        detailStartCoord.x = m_singleRoomSize.x - 1;
                        detailStartCoord.y = detailEndCoord.y;
                        break;

                    case PathDirection.Right:
                        abstractCurCoord.x += 1;

                        detailStartCoord.x = 0;
                        detailStartCoord.y = detailEndCoord.y;
                        break;
                }
                m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord.x = detailStartCoord.x;
                m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord.y = detailStartCoord.y;
            }
        }
        private void CalculateSingeDetailNodeBlockActiveTable(object inputData)
        {
            DetailCalculateInputData input = inputData as DetailCalculateInputData;
            CoordPair CoordPair = input.CoordPair;

            FastRandom random = new FastRandom((uint)(MapGenAgentInputBuffer.Instance.Data.Seed + input.CoordPair.StartCoord.x + input.CoordPair.StartCoord.y + input.CoordPair.EndCoord.x + input.CoordPair.EndCoord.y));

            Vector2Int abstractCurCoord = CoordPair.StartCoord;
            Vector2Int detailCurCoord = m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord;

            //  Abstract tracking
            while (true)
            {
                //  Detail tracking
                while (true)
                {
                    int randRad = random.Next(m_brushMinSize, m_brushMaxSize + 1);
                    int randRatio = random.Next(0, randRad);

                    for (int coord_y = 0; coord_y < randRad; coord_y++)
                    {
                        for (int coord_x = 0; coord_x < randRad; coord_x++)
                        {
                            if (detailCurCoord.y + coord_y - randRatio >= 0 && detailCurCoord.y + coord_y - randRatio < m_singleRoomSize.y && detailCurCoord.x + coord_x - randRatio >= 0 && detailCurCoord.x + coord_x - randRatio < m_singleRoomSize.x)
                            {
                                if (!(detailCurCoord.y + coord_y - randRatio == 0 && detailCurCoord.x + coord_x - randRatio == 0) && !(detailCurCoord.y + coord_y - randRatio == 0 && detailCurCoord.x + coord_x - randRatio == m_singleRoomSize.x - 1) && !(detailCurCoord.y + coord_y - randRatio == m_singleRoomSize.y - 1 && detailCurCoord.x + coord_x - randRatio == 0) && !(detailCurCoord.y + coord_y - randRatio == m_singleRoomSize.y - 1 && detailCurCoord.x + coord_x - randRatio == m_singleRoomSize.x - 1))
                                {
                                    m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].BlockActiveTable[detailCurCoord.y + coord_y - randRatio, detailCurCoord.x + coord_x - randRatio] = MapActiveType.BIsNodeActive;
                                }
                            }
                        }
                    }

                    if (detailCurCoord == m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.EndCoord)
                    {
                        break;
                    }

                    switch (m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].DivisionWayDirectionTable[detailCurCoord.y, detailCurCoord.x])
                    {
                        case PathDirection.Top:
                            detailCurCoord.y -= 1;
                            break;

                        case PathDirection.Bottom:
                            detailCurCoord.y += 1;
                            break;

                        case PathDirection.Left:
                            detailCurCoord.x -= 1;
                            break;

                        case PathDirection.Right:
                            detailCurCoord.x += 1;
                            break;
                    }
                }

                if (abstractCurCoord == CoordPair.EndCoord)
                {
                    break;
                }

                switch (m_abstractPlane[abstractCurCoord.y, abstractCurCoord.x])
                {
                    case PathDirection.Top:
                        abstractCurCoord.y -= 1;
                        break;

                    case PathDirection.Bottom:
                        abstractCurCoord.y += 1;
                        break;

                    case PathDirection.Left:
                        abstractCurCoord.x -= 1;
                        break;

                    case PathDirection.Right:
                        abstractCurCoord.x += 1;
                        break;
                }
                detailCurCoord = m_detailPlane[abstractCurCoord.y, abstractCurCoord.x].CoordPair.StartCoord;
            }

            input.ManualResetEvent.Set();
        }
        #endregion

        private float GetDistance(in Vector2Int coord1, in Vector2Int coord2)
        {
            return (float)Math.Sqrt(Math.Pow(coord1.x - coord2.x, 2.0) + Math.Pow(coord1.y - coord2.y, 2.0));
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(in bool bisDispose)
        {
            if (bisDispose)
            {
                m_abstractPlane = null;
                for (int coord_z = 0; coord_z < m_realMapSize.z; coord_z++)
                {
                    for (int coord_x = 0; coord_x < m_realMapSize.x; coord_x++)
                    {
                        m_detailPlane[coord_z, coord_x].Dispose();
                    }
                }

                m_abstractCoordPairs.Clear();
                m_abstractCoordPairs = null;

                //random = null;

                m_manualResetEvent = null;

                m_basicLayerMapGeneratorOutput.Dispose();
                m_basicLayerMapGeneratorOutput = null;
            }
        }
    }
}