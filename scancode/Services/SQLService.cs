using scancode.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace scancode.Services
{
    public class SQLService
    {
        #region lay data po
        private static string connectionString { get; } = "Data Source=192.168.122.2;Initial Catalog=MANUFASPCPD;User ID=kuser;Password=SPC123@";
        private static string queryCheckPO = @"select REQ_HED.AUFNR,REQ_HED.PHCD,REQ_HED.PHTX,REQ_HED.PSTX,REQ_HED.GAMNG,ORDER_DTL.ZGLOBAL_CODE,Packing.NW,Packing.Qty
        from [MANUFASPCPD].[dbo].[MANUFA_F_PD_DT_REQ_HED] REQ_HED
        LEFT JOIN [MANUFASPCPD].[dbo].[MANUFA_F_PD_DT_ORDER_DTL] ORDER_DTL
        ON  ORDER_DTL.VBELN = REQ_HED.KDAUF
        LEFT JOIN [F2Database].[dbo].[F2_PackingList_Main] Packing
        ON ORDER_DTL.ZGLOBAL_CODE = Packing.PoNo
        WHERE REQ_HED.AUFNR = @id";

        public ProductData GetDataForPO(string po)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand(queryCheckPO, conn);

                    //cmd.Parameters.AddWithValue("@idNV", guna_txt_id_nv.Text.ToString());
                    cmd.Parameters.AddWithValue(@"id", po.ToString().Trim());
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            Console.WriteLine("reader: " + reader["GAMNG"].ToString());
                            float.TryParse(reader["GAMNG"].ToString(), out float sluong);
                            float.TryParse(reader["NW"].ToString(), out float trongLuong);
                            return new ProductData
                            {
                                AUFNR = reader["AUFNR"].ToString(),
                                PHCD = reader["PHCD"].ToString(),
                                PHTX = reader["PHTX"].ToString(),
                                PSTX = reader["PSTX"].ToString(),
                                GAMNG = sluong,
                                NumWeight = trongLuong
                            };
                        }
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("err: " + ex);
                    return null;
                }
            }
        }


        #endregion

        #region lay data bang history
        private static string connectHistory { get; } = @"Data Source=192.168.122.2;Initial Catalog=F2Database;User ID=kproduct;Password=Tanphat@02032013";
        private static string queryGetHistory { get; } = $@"SELECT [stt]
              ,[AUFNR]
              ,[PHCD]
              ,[PHTX]
              ,[PSTX]
              ,[GAMNG]
              ,[kg]
	          ,[UPDDT]
              FROM [F2Database].[dbo].[F2_Packing_Scale_History]
              where [AUFNR] like '%' + @aufnr + '%'
              order by [stt] desc
                OFFSET @indexst ROWS
                FETCH NEXT @lengthrow ROWS ONLY
                ";
        private static string queryInsertHistory { get; } = @"
            INSERT INTO dataHistory (AUFNR, PHCD, PHTX, PSTX, GAMNG,kg,UPDDT)
            SELECT
                @AUFNR, @PHCD, @PHTX, @PSTX, @GAMNG, @kg, @UPDDT
            where not exists 
            (
            select 1
            from [F2Database].[dbo].[F2_Packing_Scale_History]
            where [AUFNR] = @AUFNR
            );

        ";
        private static string query_getCountList_history { get; } = @"SELECT COUNT(*) AS TotalRow
            FROM [dbo].[F2_Packing_Scale_History];";

        public List<dataHistory> GetListHistory(string po = "", int stIndex = 0, int length = 5 )
        {
            List<dataHistory> Lhistory = new List<dataHistory>();
            try
            {
                using (SqlConnection connect = new SqlConnection(connectHistory))
                {
                    connect.Open();
                    using (SqlCommand sqlCommand = new SqlCommand(queryGetHistory, connect))
                    {
                        sqlCommand.Parameters.AddWithValue("@aufnr", po);
                        sqlCommand.Parameters.AddWithValue("@indexst", stIndex);
                        sqlCommand.Parameters.AddWithValue("@lengthrow", length);

                        using (SqlDataReader reader = sqlCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int.TryParse(reader["stt"].ToString(), out int stt);
                                float.TryParse(reader["kg"].ToString(), out float kg);
                                DateTime.TryParse(reader["UPDDT"].ToString(), out DateTime time);
                                float.TryParse(reader["GAMNG"].ToString(), out float gamng);
                                Lhistory.Add(new dataHistory
                                {
                                    stt = stt,
                                    kg = Math.Round(kg,2),
                                    UPDDT = time,
                                    AUFNR = reader["AUFNR"].ToString(),
                                    PHCD = reader["PHCD"].ToString(),
                                    PHTX = reader["PHTX"].ToString(),
                                    PSTX = reader["PSTX"].ToString(),
                                    GAMNG = gamng
                                });
                            }
                        }
                    }
                }
                Console.WriteLine("sl: "+Lhistory.Count);
                return Lhistory;
            }
            catch
            {
                return null;
            }
        }

        public bool insertDataHistory(dataHistory data)
        {
            try
            {
                using (SqlConnection connect = new SqlConnection(connectHistory))
                {
                    connect.Open();
                    using(SqlCommand cmd = new SqlCommand(queryInsertHistory,connect))
                    {
                        cmd.Parameters.AddWithValue("@AUFNR",data.AUFNR);
                        cmd.Parameters.AddWithValue("@PHCD", data.PHCD);
                        cmd.Parameters.AddWithValue("@PHTX", data.PHTX);
                        cmd.Parameters.AddWithValue("@PSTX", data.PSTX);
                        cmd.Parameters.AddWithValue("@GAMNG", data.GAMNG);
                        cmd.Parameters.AddWithValue("@kg", data.kg);
                        cmd.Parameters.AddWithValue("@UPDDT", data.UPDDT);


                        int rowReusult = cmd.ExecuteNonQuery();
                        return rowReusult > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public int getCoutListHistory()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectHistory))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query_getCountList_history,conn))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader() )
                        {
                            while (reader.Read())
                            {
                                int.TryParse(reader["TotalRow"].ToString(), out int countList);
                                return countList;
                            }
                        }

                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        #endregion


    }
}
