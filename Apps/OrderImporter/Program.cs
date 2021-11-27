using System;
using System.IO;
using System.Xml;
using System.Threading;
using System.Reflection;
using MySql.Data.MySqlClient;
using ESD = ESunny.Domestic.Trade;
using ESG = ESunny.Global.Trade;

namespace OrderImporter
{
  internal class Program
  {
    private static MySqlConnection connection;
    private static MySqlTransaction transaction;
    private static string currentDirectory;

    private static void Main(string[] args)
    {
      try
      {
        if (args.Length == 0)
          Console.WriteLine("#ERR: Please specify the command line argument (Domestic or Global) where the app is running");

        var isInsideGreatWall = args[0] == "Domestic";

        var config = new XmlDocument();
        config.Load(Assembly.GetEntryAssembly().Location + ".user.config");

        currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        connection = new MySqlConnection(config.SelectNodes("configuration/database").Item(0).Attributes[0].Value);
        connection.Open();
        transaction = connection.BeginTransaction();
        Console.WriteLine("Order importer started!");

        foreach (XmlElement api in config.SelectNodes("configuration/api/instance"))
        {
          if (api.GetAttribute("type") == "ESunnyDomestic")
          {
            if (isInsideGreatWall)
            {
              ProcessESunnyDomestic(
                api.GetAttribute("ip"),
                Convert.ToUInt16(api.GetAttribute("port")),
                api.GetAttribute("userNo"),
                api.GetAttribute("password"),
                api.GetAttribute("appId"),
                api.GetAttribute("authCode"));
            }
            else
            {
              ProcessESunnyDomesticFiles();
            }
          }
          else if (api.GetAttribute("type") == "ESunnyGlobal")
          {
            if (!isInsideGreatWall)
            {
              ProcessESunnyGlobal(
                api.GetAttribute("ip"),
                Convert.ToUInt16(api.GetAttribute("port")),
                api.GetAttribute("userNo"),
                api.GetAttribute("password"),
                api.GetAttribute("contactInfo"),
                api.GetAttribute("authCode"));
            }
          }
          else
            Console.WriteLine("#ERR: Unsupported API type (must be ESunnyDomestic or ESunnyGlobal)");
        }

        transaction.Commit();
        connection.Close();
        Console.WriteLine("Order importer finished!");
      }
      catch (Exception ex)
      {
        Console.WriteLine("#ERR: " + ex.Message);
      }
    }

    private static void ProcessESunnyDomestic(string ip, ushort port, string userNo, string password, string appId, string authCode)
    {
      Console.WriteLine("Save ESunny domestic trade data for account {0} to files ...", userNo);

      // Create and intialize API
      var appInfo = new ESD.TapAPIApplicationInfo
      {
        AuthCode = authCode,
        KeyOperationLogPath = Path.GetTempPath()
      };

      var api = ESD.External.CreateTapTradeAPI(appInfo, out int errorCode);
      if (api == null)
      {
        Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to create API: null pointer", userNo);
        return;
      }

      void cleanup()
      {
        ESD.External.FreeTapTradeAPI(api);
        api = null;
      }

      if (api == null || errorCode != ESD.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to create API: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      errorCode = api.SetHostAddress(ip, port);
      if (errorCode != ESD.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to set host address: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      var callback = new ESD.EventCallback();
      api.SetAPINotify(callback);

      var resetEvent1 = new ManualResetEvent(false);
      var resetEvent2 = new ManualResetEvent(false);

      // Handler for OnRspLogin event
      callback.OnRspLoginEvent += (errCode, loginRspInfo) =>
      {
        if (errCode != ESD.External.TAPIERROR_SUCCEED && errCode != ESD.External.TAPIERROR_LOGIN_DDA) // ignore TAPIERROR_LOGIN_DDA
        {
          Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to login: error code is {1}", userNo, errCode);
          resetEvent1.Set();
          resetEvent2.Set();
        }
      };

      // Handler for OnAPIReady event
      callback.OnAPIReadyEvent += () =>
      {
        var posQryReq = new ESD.TapAPIPositionQryReq();
        api.QryPosition(out uint sessionId, posQryReq);

        var fillQryReq = new ESD.TapAPIFillQryReq();
        api.QryFill(out sessionId, fillQryReq);
      };

      // Handler for OnRspQryPosition event
      var asOf = DateTime.Now;
      callback.OnRspQryPositionEvent += (sessionId, errCode, isLast, info) =>
      {
        if (errCode != ESD.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to query position: error code is {1}", userNo, errCode);
          resetEvent1.Set();
        }
        else if (info != null)
        {
          var multiplier = (info.MatchSide == ESD.External.TAPI_SIDE_BUY) ? 1 : -1;
          var quantity = multiplier * (int)info.PositionQty;

          using (StreamWriter file = new StreamWriter(currentDirectory + "\\Position.txt", true))
          {
            var line = string.Format("{0},{1},{2},{3}",
              asOf, info.AccountNo, string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }), quantity);

            file.WriteLine(line);
            Console.WriteLine(line);
          }

          if (isLast == ESD.External.APIYNFLAG_YES)
            resetEvent1.Set();
        }
        else
          resetEvent1.Set();
      };

      // Handler for OnRspQryFill event
      callback.OnRspQryFillEvent += (sessionId, errCode, isLast, info) =>
      {
        if (errCode != ESD.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to query fill summary: error code is {1}", userNo, errCode);
          resetEvent2.Set();
        }
        else if (info != null)
        {
          using (StreamWriter file = new StreamWriter(currentDirectory + "\\Fill.txt", true))
          {
            var line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
              info.AccountNo, info.OrderNo, info.MatchNo,
              string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }),
              info.MatchSide, info.PositionEffect, info.MatchQty, info.MatchPrice, info.FeeValue, info.MatchDateTime);

            file.WriteLine(line);
            Console.WriteLine(line);
          }

          if (isLast == ESD.External.APIYNFLAG_YES)
            resetEvent2.Set();
        }
        else
          resetEvent2.Set();
      };

      // Login to the API
      var loginAuth = new ESD.TapAPITradeLoginAuth
      {
        UserNo = userNo,
        Password = password,
        AuthCode = authCode,
        AppID = appId,
        ISModifyPassword = ESD.External.APIYNFLAG_NO,
        ISDDA = ESD.External.APIYNFLAG_NO
      };

      errorCode = api.Login(loginAuth);
      if (errorCode != ESD.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Domestic Account {0}: Failed to login: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      resetEvent1.WaitOne();
      resetEvent2.WaitOne();

      cleanup();
    }

    private static void ProcessESunnyDomesticFiles()
    {
      Console.WriteLine("Load ESunny domestic trade data to database ...");

      try
      {
        using (StreamReader file = new StreamReader(currentDirectory + "\\Position.txt"))
        {
          string line;
          while ((line = file.ReadLine()) != null)
          {
            var paramArray = line.Split(',');
            UpdatePositionSQL(DateTime.Parse(paramArray[0]), paramArray[1], paramArray[2], Int32.Parse(paramArray[3]), false);

            Console.WriteLine(line);
          }
        }

        File.Delete(currentDirectory + "\\Position.txt");

        using (StreamReader file = new StreamReader(currentDirectory + "\\Fill.txt"))
        {
          string line;
          while ((line = file.ReadLine()) != null)
          {
            var paramArray = line.Split(',');
            UpdateFillSQL(paramArray[0], paramArray[1], paramArray[2], paramArray[3], paramArray[4][0], paramArray[5][0],
              Int32.Parse(paramArray[6]), Double.Parse(paramArray[7]), Double.Parse(paramArray[8]), DateTime.Parse(paramArray[9]));

            Console.WriteLine(line);
          }
        }

        File.Delete(currentDirectory + "\\Fill.txt");
      }
      catch (FileNotFoundException ex)
      {
        Console.WriteLine("#ERR: " + ex.Message);
      }
    }

    private static void ProcessESunnyGlobal(string ip, ushort port, string userNo, string password, string contactInfo, string authCode)
    {
      Console.WriteLine("Load ESunny global trade data for account {0} to database ...", userNo);

      // Create and intialize API
      var appInfo = new ESG.TapAPIApplicationInfo
      {
        AuthCode = authCode,
        KeyOperationLogPath = Path.GetTempPath()
      };

      var api = ESG.External.CreateITapTradeAPI(appInfo, out int errorCode);
      if (api == null)
      {
        Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to create API: null pointer", userNo);
        return;
      }

      void cleanup()
      {
        ESG.External.FreeITapTradeAPI(api);
        api = null;
      }

      if (errorCode != ESG.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to create API: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      errorCode = api.SetHostAddress(ip, port);
      if (errorCode != ESG.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to set host address: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      var callback = new ESG.EventCallback();
      api.SetAPINotify(callback);

      var resetEvent1 = new ManualResetEvent(false);
      var resetEvent2 = new ManualResetEvent(false);

      // Handler for OnRspLogin event
      callback.OnRspLoginEvent += (errCode, loginRspInfo) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED && errCode != ESG.External.TAPIERROR_LOGIN_DDA) // ignore TAPIERROR_LOGIN_DDA
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to login: error code is {1}", userNo, errCode);
          resetEvent1.Set();
          resetEvent2.Set();
        }
      };

      // Handler for OnRtnContactInfo event
      callback.OnRtnContactInfoEvent += (errCode, isLast, contactInfo_) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to get contact info: error code is {1}", userNo, errCode);
          resetEvent1.Set();
          resetEvent2.Set();
        }
        else
        {
          if (contactInfo_ == contactInfo)
            api.RequestVertificateCode(out uint sessionId, contactInfo_);
          else
          {
            Console.WriteLine("#ERR: ES Gobal Account {0}: Unknown verification contact {1}", userNo, contactInfo_);
            resetEvent1.Set();
            resetEvent2.Set();
          }
        }
      };

      // Handler for OnRspRequestVertificateCode event
      callback.OnRspRequestVertificateCodeEvent += (sessionId, errCode, rsp) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to request verification code: error code is {1}", userNo, errCode);
          resetEvent1.Set();
          resetEvent2.Set();
        }
        else
        {
          Console.WriteLine("Gobal Account {0}: Please type verification code to log onto ESunny Global Trade API:", userNo);
          var req = new ESG.TapAPISecondCertificationReq { VertificateCode = Console.ReadLine(), LoginType = ESG.External.TAPI_LOGINTYPE_NORMAL };
          api.SetVertificateCode(out sessionId, req);
        }
      };

      // Handler for OnAPIReady event
      callback.OnAPIReadyEvent += (errCode) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: API is not ready: error code is {1}", userNo, errCode);
          resetEvent1.Set();
          resetEvent2.Set();
        }
        else
        {
          var posQryReq = new ESG.TapAPIPositionQryReq { AccountNo = userNo };
          api.QryPositionSummary(out uint sessionId, posQryReq);

          var fillQryReq = new ESG.TapAPIFillQryReq { AccountNo = userNo };
          api.QryFill(out sessionId, fillQryReq);
        }
      };

      // Handler for OnRspQryPositionSummary event
      var asOf = DateTime.Now;
      callback.OnRspQryPositionSummaryEvent += (sessionId, errCode, isLast, info) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to query position summary: error code is {1}", userNo, errCode);
          resetEvent1.Set();
        }
        else if (info != null)
        {
          var multiplier = (info.MatchSide == ESG.External.TAPI_SIDE_BUY) ? 1 : -1;
          var quantity = multiplier * (int)info.PositionQty;

          UpdatePositionSQL(asOf, info.AccountNo, string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }), quantity, true);

          Console.WriteLine("{0},{1},{2},{3}",
            asOf, info.AccountNo, string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }), quantity);

          if (isLast == ESG.External.APIYNFLAG_YES)
            resetEvent1.Set();
        }
        else
          resetEvent1.Set();
      };

      // Handler for OnRspQryFillEvent event
      callback.OnRspQryFillEvent += (sessionId, errCode, isLast, info) =>
      {
        if (errCode != ESG.External.TAPIERROR_SUCCEED)
        {
          Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to query position summary: error code is {1}", userNo, errCode);
          resetEvent2.Set();
        }
        else if (info != null)
        {
          UpdateFillSQL(info.AccountNo, info.OrderNo, info.MatchNo,
            string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }),
            info.MatchSide, info.PositionEffect, (int)info.MatchQty, info.MatchPrice, info.FeeValue, DateTime.Parse(info.MatchDateTime));

          Console.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
            info.AccountNo, info.OrderNo, info.MatchNo,
            string.Join("-", new string[] { info.ExchangeNo, info.CommodityNo, info.ContractNo }),
            info.MatchSide, info.PositionEffect, (int)info.MatchQty, info.MatchPrice, info.FeeValue, DateTime.Parse(info.MatchDateTime));

          if (isLast == ESG.External.APIYNFLAG_YES)
            resetEvent2.Set();
        }
        else
          resetEvent2.Set();
      };

      // Login to the API
      var loginAuth = new ESG.TapAPITradeLoginAuth
      {
        UserNo = userNo,
        Password = password,
        ISModifyPassword = ESG.External.APIYNFLAG_NO
      };

      errorCode = api.Login(loginAuth);
      if (errorCode != ESG.External.TAPIERROR_SUCCEED)
      {
        Console.WriteLine("#ERR: ES Gobal Account {0}: Failed to login: error code is {1}", userNo, errorCode);
        cleanup();
        return;
      }

      resetEvent1.WaitOne();
      resetEvent2.WaitOne();

      cleanup();
    }

    private static void UpdateFillSQL(string account, string orderNo, string matchNo, string contractCode,
      char buySell, char openCover, int quantity, double price, double fees, DateTime timestamp)
    {
      var cmd = connection.CreateCommand();
      cmd.Transaction = transaction;
      cmd.CommandText = "REPLACE INTO trade_details VALUES (@account, @orderNo, @matchNo, " +
        "@contractCode, @buySell, @openCover, @quantity, @price, @fees, @timestamp)";

      cmd.Parameters.AddWithValue("@account", account);
      cmd.Parameters.AddWithValue("@orderNo", orderNo);
      cmd.Parameters.AddWithValue("@matchNo", matchNo);
      cmd.Parameters.AddWithValue("@contractCode", contractCode);
      cmd.Parameters.AddWithValue("@buySell", buySell);
      cmd.Parameters.AddWithValue("@openCover", openCover);
      cmd.Parameters.AddWithValue("@quantity", quantity);
      cmd.Parameters.AddWithValue("@price", price);
      cmd.Parameters.AddWithValue("@fees", fees);
      cmd.Parameters.AddWithValue("@timestamp", timestamp);

      cmd.ExecuteNonQuery();
    }

    private static void UpdatePositionSQL(DateTime asOf, string account, string contractCode, int quantity, bool isSummary)
    {
      var cmd = connection.CreateCommand();
      cmd.Transaction = transaction;
      cmd.CommandText = "INSERT INTO position VALUES (@asOf, @account, @contractCode, @quantity) ON DUPLICATE KEY UPDATE ";
      if (isSummary)
        cmd.CommandText += "quantity = @quantity";
      else
        cmd.CommandText += "quantity = quantity + @quantity";

      cmd.Parameters.AddWithValue("@asOf", asOf);
      cmd.Parameters.AddWithValue("@account", account);
      cmd.Parameters.AddWithValue("@contractCode", contractCode);
      cmd.Parameters.AddWithValue("@quantity", quantity);

      cmd.ExecuteNonQuery();
    }
  }
}
