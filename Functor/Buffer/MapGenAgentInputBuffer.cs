using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Unite2026Seoul_MapGenCommonLib;

using Newtonsoft.Json;


namespace Unite2026Seoul_MapGenAgent
{
    internal sealed class MapGenAgentInputBuffer
    {
        private static MapGenAgentInputBuffer m_instance;

        private MapGenAgentInputData m_data;


        internal static MapGenAgentInputBuffer Instance
        {
            get
            {
                if(m_instance == null)
                {
                    m_instance = new MapGenAgentInputBuffer();
                }
                return m_instance;
            }
        }

        internal MapGenAgentInputData Data
        {
            get
            {
                return m_data;
            }
            set
            {
                m_data = value;
            }
        }

        internal void LoadData(in string jsonData)
        {
            try
            {
                m_data = JsonConvert.DeserializeObject<MapGenAgentInputData>(jsonData);
            }
            catch
            {
                // TODO - Unity와의 IPC 과정에서 오류 고지해야 함
            }
        }
#if UNIT_TEST
        internal void LoadData(in MapGenAgentInputData data)
        {
            m_data = data;
        }
#endif
    }
}