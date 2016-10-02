﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;

namespace SqlSugar
{
    /// <summary>
    /// SqlSugarTool局部类存放具有拼接SQL的函数
    /// </summary>
    public partial class SqlSugarTool
    {
        /// <summary>
        /// 包装SQL
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="shortName"></param>
        /// <returns></returns>
        internal static string PackagingSQL(string sql, string shortName)
        {
            return string.Format(" SELECT * FROM ({0}) {1} ", sql, shortName);
        }

        internal static StringBuilder GetQueryableSql<T>(SqlSugar.Queryable<T> queryable)
        {
            string joinInfo = string.Join(" ", queryable.JoinTableValue);
            StringBuilder sbSql = new StringBuilder();
            string tableName = queryable.TableName.IsNullOrEmpty() ? queryable.TName : queryable.TableName;
            if (queryable.DB.Language.IsValuable() && queryable.DB.Language.Suffix.IsValuable())
            {
                var viewNameList = LanguageHelper.GetLanguageViewNameList(queryable.DB);
                var isLanView = viewNameList.IsValuable() && viewNameList.Any(it => it == tableName);
                if (!queryable.DB.Language.Suffix.StartsWith(LanguageHelper.PreSuffix))
                {
                    queryable.DB.Language.Suffix = LanguageHelper.PreSuffix + queryable.DB.Language.Suffix;
                }

                //将视图变更为多语言的视图
                if (isLanView)
                    tableName = typeof(T).Name + queryable.DB.Language.Suffix;
            }
            if (queryable.DB.PageModel == PageModel.RowNumber)
            {
                #region  rowNumber
                string withNoLock = queryable.DB.IsNoLock ? "WITH(NOLOCK)" : null;
                var order = queryable.OrderByValue.IsValuable() ? (",row_index=ROW_NUMBER() OVER(ORDER BY " + queryable.OrderByValue + " )") : null;

                sbSql.AppendFormat("SELECT " + queryable.SelectValue.GetSelectFiles() + " {1} FROM [{0}] {5} {2} WHERE 1=1 {3} {4} ", tableName, order, withNoLock, string.Join("", queryable.WhereValue), queryable.GroupByValue.GetGroupBy(), joinInfo);
                if (queryable.Skip == null && queryable.Take != null)
                {
                    if (joinInfo.IsValuable())
                    {
                        sbSql.Insert(0, "SELECT * FROM ( ");
                    }
                    else
                    {
                        sbSql.Insert(0, "SELECT " + queryable.SelectValue.GetSelectFiles() + " FROM ( ");
                    }
                    sbSql.Append(") t WHERE t.row_index<=" + queryable.Take);
                }
                else if (queryable.Skip != null && queryable.Take == null)
                {
                    if (joinInfo.IsValuable())
                    {
                        sbSql.Insert(0, "SELECT * FROM ( ");
                    }
                    else
                    {
                        sbSql.Insert(0, "SELECT " + queryable.SelectValue.GetSelectFiles() + " FROM ( ");
                    }
                    sbSql.Append(") t WHERE t.row_index>" + (queryable.Skip));
                }
                else if (queryable.Skip != null && queryable.Take != null)
                {
                    if (joinInfo.IsValuable())
                    {
                        sbSql.Insert(0, "SELECT * FROM ( ");
                    }
                    else
                    {
                        sbSql.Insert(0, "SELECT " + queryable.SelectValue.GetSelectFiles() + " FROM ( ");
                    }
                    sbSql.Append(") t WHERE t.row_index BETWEEN " + (queryable.Skip + 1) + " AND " + (queryable.Skip + queryable.Take));
                }
                #endregion
            }
            else
            {

                #region offset
                string withNoLock = queryable.DB.IsNoLock ? "WITH(NOLOCK)" : null;
                var order = queryable.OrderByValue.IsValuable() ? ("ORDER BY " + queryable.OrderByValue + " ") : null;
                sbSql.AppendFormat("SELECT " + queryable.SelectValue.GetSelectFiles() + " {1} FROM [{0}] {5} {2} WHERE 1=1 {3} {4} ", tableName, "", withNoLock, string.Join("", queryable.WhereValue), queryable.GroupByValue.GetGroupBy(), joinInfo);
                sbSql.Append(order);
                if (queryable.Skip != null || queryable.Take != null)
                {
                    sbSql.AppendFormat("OFFSET {0} ROW FETCH NEXT {1} ROWS ONLY", Convert.ToInt32(queryable.Skip), Convert.ToInt32(queryable.Take));
                }
                #endregion
            }
            return sbSql;
        }

        internal static void GetSqlableSql(Sqlable sqlable, string fileds, string orderByFiled, int pageIndex, int pageSize, StringBuilder sbSql)
        {
            if (sqlable.DB.PageModel == PageModel.RowNumber)
            {
                sbSql.Insert(0, string.Format("SELECT {0},row_index=ROW_NUMBER() OVER(ORDER BY {1} )", fileds, orderByFiled));
                sbSql.Append(" WHERE 1=1 ").Append(string.Join(" ", sqlable.Where));
                sbSql.Append(sqlable.OrderBy);
                sbSql.Append(sqlable.GroupBy);
                int skip = (pageIndex - 1) * pageSize + 1;
                int take = pageSize;
                sbSql.Insert(0, "SELECT * FROM ( ");
                sbSql.AppendFormat(") t WHERE  t.row_index BETWEEN {0}  AND {1}   ", skip, skip + take - 1);
            }
            else
            {
                sbSql.Insert(0, string.Format("SELECT {0}", fileds));
                sbSql.Append(" WHERE 1=1 ").Append(string.Join(" ", sqlable.Where));
                sbSql.Append(sqlable.GroupBy);
                sbSql.AppendFormat(" ORDER BY {0} ", orderByFiled);
                int skip = (pageIndex - 1) * pageSize;
                int take = pageSize;
                sbSql.AppendFormat("OFFSET {0} ROW FETCH NEXT {1} ROWS ONLY", skip, take);
            }
        }

        /// <summary>
        /// 获取 WITH(NOLOCK)
        /// </summary>
        /// <param name="isNoLock"></param>
        /// <returns></returns>
        public static string GetLockString(bool isNoLock)
        {
            return isNoLock ? " WITH(NOLOCK) " : "";
        }

        /// <summary>
        /// 根据表获取主键
        /// </summary>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        internal static string GetPrimaryKeyByTableName(SqlSugarClient db, string tableName)
        {
            string key = "GetPrimaryKeyByTableName" + tableName;
            tableName = tableName.ToLower();
            var cm = CacheManager<List<KeyValue>>.GetInstance();
            List<KeyValue> primaryInfo = null;

            //获取主键信息
            if (cm.ContainsKey(key))
                primaryInfo = cm[key];
            else
            {
                string sql = @"  				SELECT a.name as keyName ,d.name as tableName
  FROM   syscolumns a 
  inner  join sysobjects d on a.id=d.id       
  where  exists(SELECT 1 FROM sysobjects where xtype='PK' and  parent_obj=a.id and name in (  
  SELECT name  FROM sysindexes   WHERE indid in(  
  SELECT indid FROM sysindexkeys WHERE id = a.id AND colid=a.colid  
)))";
                var isLog = db.IsEnableLogEvent;
                db.IsEnableLogEvent = false;
                var dt = db.GetDataTable(sql);
                db.IsEnableLogEvent = isLog;
                primaryInfo = new List<KeyValue>();
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow dr in dt.Rows)
                    {
                        primaryInfo.Add(new KeyValue() { Key = dr["tableName"].ToString().ToLower(), Value = dr["keyName"].ToString() });
                    }
                }
                cm.Add(key, primaryInfo, cm.Day);
            }

            //反回主键
            if (!primaryInfo.Any(it => it.Key == tableName))
            {
                return null;
            }
            return primaryInfo.First(it => it.Key == tableName).Value;

        }

        /// <summary>
        ///根据表名获取自添列 keyTableName Value columnName
        /// </summary>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        internal static List<KeyValue> GetIdentitiesKeyByTableName(SqlSugarClient db, string tableName)
        {
            string key = "GetIdentityKeyByTableName" + tableName;
            var cm = CacheManager<List<KeyValue>>.GetInstance();
            List<KeyValue> identityInfo = null;
            string sql = string.Format(@"
                            declare @Table_name varchar(60)
                            set @Table_name = '{0}';


                            Select so.name tableName,                   --表名字
                                   sc.name keyName,             --自增字段名字
                                   ident_current(so.name) curr_value,    --自增字段当前值
                                   ident_incr(so.name) incr_value,       --自增字段增长值
                                   ident_seed(so.name) seed_value        --自增字段种子值
                              from sysobjects so 
                            Inner Join syscolumns sc
                                on so.id = sc.id

                                   and columnproperty(sc.id, sc.name, 'IsIdentity') = 1

                            Where upper(so.name) = upper(@Table_name)
         ", tableName);
            if (cm.ContainsKey(key))
            {
                identityInfo = cm[key];
                return identityInfo;
            }
            else
            {
                var isLog = db.IsEnableLogEvent;
                db.IsEnableLogEvent = false;
                var dt = db.GetDataTable(sql);
                db.IsEnableLogEvent = isLog;
                identityInfo = new List<KeyValue>();
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow dr in dt.Rows)
                    {
                        identityInfo.Add(new KeyValue() { Key = dr["tableName"].ToString().ToLower(), Value = dr["keyName"].ToString() });
                    }
                }
                cm.Add(key, identityInfo, cm.Day);
                return identityInfo;
            }
        }

        /// <summary>
        /// 根据表名获取列名
        /// </summary>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        internal static List<string> GetColumnsByTableName(SqlSugarClient db, string tableName)
        {
            string key = "GetColumnNamesByTableName" + tableName;
            var cm = CacheManager<List<string>>.GetInstance();
            if (cm.ContainsKey(key))
            {
                return cm[key];
            }
            else
            {
                var isLog = db.IsEnableLogEvent;
                db.IsEnableLogEvent = false;
                string sql = " SELECT Name FROM SysColumns WHERE id=Object_Id('" + tableName + "')";
                var reval = db.SqlQuery<string>(sql);
                db.IsEnableLogEvent = isLog;
                cm.Add(key, reval, cm.Day);
                return reval;
            }
        }
    }
}