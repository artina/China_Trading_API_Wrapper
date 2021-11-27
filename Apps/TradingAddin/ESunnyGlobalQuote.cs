using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExcelDna.Integration;
using ExcelDna.Integration.Rtd;
using TradingAddin.Properties;

namespace ESunny.Global.Quote
{
  // Contains the API singleton
  public static class APIInstance
  {
    public static ITapQuoteAPI api = null;
    public static EventCallback callback = null;

    private static object isConnected = false;
    private static string errorText = "";
    private static ManualResetEvent resetEvent = new ManualResetEvent(false);

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.LoginQuote", Description = "Login to API")]
    public static object Login()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null) return "API is ready.";

      // Create and intialize API
      var appInfo = new TapAPIApplicationInfo
      {
        AuthCode = Settings.Default.ESunny_Global_Quote_AuthCode,
        KeyOperationLogPath = Path.GetTempPath()
      };

      void cleanup()
      {
        if (api != null) External.FreeTapQuoteAPI(api);
        api = null;
      }

      api = External.CreateTapQuoteAPI(appInfo, out int errorCode);
      if (api == null || errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to create API: error code is " + errorCode;
      }

      var ip = Settings.Default.ESunny_Global_Quote_IP;
      var port = Settings.Default.ESunny_Global_Quote_Port;
      errorCode = api.SetHostAddress(ip, port);
      if (errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to set host address: error code is " + errorCode;
      }

      if (callback != null)
      {
        resetEvent.Reset(); // reset to nonsignaled, causing threads to block
        api.SetAPINotify(callback); // reuse callback when API is reconnected (i.e., set to null)
      }
      else
      {
        callback = new EventCallback();
        api.SetAPINotify(callback);

        // Handler for OnRspLogin event
        callback.OnRspLoginEvent += (errCode, loginRspInfo) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
          {
            errorText = "#ERR: Failed to login: error code is " + errCode;
            resetEvent.Set();
          }
        };

        // Handler for OnAPIReady event
        callback.OnAPIReadyEvent += () =>
        {
          resetEvent.Set();
        };

        // Handler for OnDisconnect event
        callback.OnDisconnectEvent += (reasonCode) =>
        {
          errorText = "#ERR: API disconnected: reason code is " + reasonCode;
          lock (isConnected) isConnected = false;
          resetEvent.Set();
          cleanup();
        };
      }

      // Login to the API
      var loginAuth = new TapAPIQuoteLoginAuth
      {
        UserNo = Settings.Default.ESunny_Global_Quote_UserNo,
        Password = Settings.Default.ESunny_Global_Quote_Password,
        ISModifyPassword = External.APIYNFLAG_NO,
        ISDDA = External.APIYNFLAG_NO
      };

      errorCode = api.Login(loginAuth);
      if (errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to login: error code is " + errorCode;
      }

      resetEvent.WaitOne();

      if (errorText == "")
      {
        isConnected = true;
        return "API is ready.";
      }

      cleanup();
      return errorText;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.LogoutQuote", Description = "Log out API")]
    public static object Logout()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null)
      {
        External.FreeTapQuoteAPI(api);
        api = null;
      }

      return "API is logged out.";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.QuoteConnected",
      Description = "Check quote connection")]
    public static object QuoteConnected()
    {
      lock (isConnected) return isConnected;
    }
  }

  public static class APIFunctions
  {
    public static readonly string[] scalarFields = new string[] { "DateTimeStamp", "QLastPrice", "QLastQty",
      "QImpliedBidPrice", "QImpliedBidQty", "QImpliedAskPrice", "QImpliedAskQty" };
    public static readonly string[] vectorFields = new string[] { "QBidPrice", "QBidQty", "QAskPrice", "QAskQty" };
    public static readonly int marketDepth = 20;
    public static readonly string[] allFields = GetAllFields();

    private static string[] GetAllFields()
    {
      var result = new List<string>();

      for (var i = 0; i < scalarFields.Length; ++i)
      {
        result.Add(scalarFields[i]);
      }

      for (var i = 0; i < vectorFields.Length; ++i)
      {
        for (var j = 0; j < marketDepth; ++j)
        {
          result.Add(vectorFields[i] + (j + 1));
        }
      }

      return result.ToArray();
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.GetQuoteDetails", Description = "Get quote details")]
    public static object GetQuoteDetails(string exchange, string commodityNo, string contractNo, string field)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      if (Array.IndexOf(allFields, field) == -1) return "#ERR: Invalid field.";

      return XlCall.RTD(ApiRtdServer.ServerProgId, null, new string[] { exchange, commodityNo, contractNo, field });
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.GetQuoteFields", Description = "Get supported quote fields")]
    public static object GetQuoteFields()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      var result = new string[allFields.Length, 1];

      for (var i = 0; i < allFields.Length; ++i)
      {
        result[i, 0] = allFields[i];
      }

      return result;
    }
  }

  [ComVisible(true)]
  [ProgId(ServerProgId)]
  public class ApiRtdServer : ExcelRtdServer
  {
    public const string ServerProgId = "ApiRtdServer.ESunnyGlobalQuote";

    private Dictionary<string, Topic> topicMap; // contractCode + field => topic
    private Dictionary<string, List<object>> quoteMap; // contractCode => list of quote values
    private static List<TapAPIContract> subscribedContracts = new List<TapAPIContract>(); // for re-subscriptions on API re-connection

    public static void ReSubscribeGlobalQuotes()
    {
      lock (subscribedContracts)
      {
        foreach (var contract in subscribedContracts)
        {
          APIInstance.api.SubscribeQuote(out uint sessionId, contract);
        }
      }
    }

    protected override bool ServerStart()
    {
      topicMap = new Dictionary<string, Topic>();
      quoteMap = new Dictionary<string, List<object>>();

      // Quote handler used by 2 events: OnRspSubscribeQuote & OnRtnQuote
      void QuoteHandler(TapAPIQuoteWhole info)
      {
        var contractCode = info.Contract.Commodity.ExchangeNo + info.Contract.Commodity.CommodityNo + info.Contract.ContractNo1;
        if (info.Contract.ContractNo2 != "")
          contractCode += "/" + info.Contract.ContractNo2;

        var quoteInfo = new List<object>();

        foreach (var field in APIFunctions.scalarFields)
        {
          var value = info.GetType().GetProperty(field).GetValue(info);
          quoteInfo.Add(value);

          var topicKey = contractCode + field;
          if (topicMap.ContainsKey(topicKey))
            topicMap[topicKey].UpdateValue(value);
        }

        foreach (var field in APIFunctions.vectorFields)
        {
          var values = info.GetType().GetProperty(field).GetValue(info);

          for (var i = 0; i < APIFunctions.marketDepth; ++i)
          {
            object value = 0.0;

            if (values.GetType() == typeof(SWIGTYPE_p_double))
              value = External.ArrayUtils_double_getitem((SWIGTYPE_p_double)values, i);
            else if (values.GetType() == typeof(SWIGTYPE_p_unsigned_long_long))
              value = External.ArrayUtils_ulong_getitem((SWIGTYPE_p_unsigned_long_long)values, i);

            quoteInfo.Add(value);

            var topicKey = contractCode + field + (i + 1);
            if (topicMap.ContainsKey(topicKey))
              topicMap[topicKey].UpdateValue(value);
          }
        }

        quoteMap[contractCode] = quoteInfo;
      }

      APIInstance.callback.OnRspSubscribeQuoteEvent += (sessionID, errorCode, isLast, info) =>
      {
        if (errorCode != External.TAPIERROR_SUCCEED)
          new Thread(() => MessageBox.Show("#ERR: OnRspSubscribeQuote: error code is " + errorCode)).Start();
        else
          QuoteHandler(info);
      };

      APIInstance.callback.OnRtnQuoteEvent += QuoteHandler;

      return true;
    }

    protected override void ServerTerminate()
    {
      topicMap.Clear();
      quoteMap.Clear();
    }

    protected override object ConnectData(Topic topic, IList<string> topicInfo, ref bool newValues)
    {
      var contractCode = topicInfo[0] + topicInfo[1] + topicInfo[2];
      var field = topicInfo[3];
      var topicKey = contractCode + field;

      if (!topicMap.ContainsKey(topicKey))
        topicMap.Add(topicKey, topic);

      if (quoteMap.ContainsKey(contractCode))
      {
        var fieldIndex = Array.IndexOf(APIFunctions.allFields, field);
        var value = quoteMap[contractCode][fieldIndex];

        topicMap[topicKey].UpdateValue(value);
        return value;
      }
      else
      {
        var commodityType = External.TAPI_COMMODITY_TYPE_FUTURES;
        if (topicInfo[2].Contains("/"))
          commodityType = External.TAPI_COMMODITY_TYPE_SPREAD_MONTH;
        else if (topicInfo[2] == "INDEX")
          commodityType = External.TAPI_COMMODITY_TYPE_SPOT;

        var contract = new TapAPIContract
        {
          Commodity = new TapAPICommodity
          {
            ExchangeNo = topicInfo[0],
            CommodityType = commodityType,
            CommodityNo = topicInfo[1]
          },
          ContractNo1 = topicInfo[2].Contains("/") ? topicInfo[2].Split('/')[0] : topicInfo[2],
          ContractNo2 = topicInfo[2].Contains("/") ? topicInfo[2].Split('/')[1] : "",
          CallOrPutFlag1 = External.TAPI_CALLPUT_FLAG_NONE,
          CallOrPutFlag2 = External.TAPI_CALLPUT_FLAG_NONE
        };

        var errorCode = APIInstance.api.SubscribeQuote(out uint sessionId, contract);

        if (errorCode != External.TAPIERROR_SUCCEED)
        {
          var msg = "#ERR: Quote RTD failed for [" + string.Join(",", topicInfo) + "]: error code is " + errorCode;
          new Thread(() => MessageBox.Show(msg)).Start();
        }
        else
        {
          lock (subscribedContracts)
            subscribedContracts.Add(contract);
        }

        return 0.0;
      }
    }
  }
}
