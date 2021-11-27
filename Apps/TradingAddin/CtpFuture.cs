using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExcelDna.Integration;
using ExcelDna.Integration.Rtd;
using TradingAddin.Properties;

namespace CTP.Future
{
  // Contains the API singleton
  public static class APIInstance
  {
    public static CThostFtdcTraderApi api = null;
    public static CThostFtdcMdApi mdApi = null;

    public static CThostFtdcTraderSpiEventCallback callback = null;
    public static CThostFtdcMdSpiEventCallback mdCallback = null;

    private static object isTraderApiConnected = false;
    private static object isMdApiConnected = false;

    private static string errorText = "";
    private static string mdErrorText = "";

    private static AutoResetEvent resetEvent = new AutoResetEvent(false);
    private static AutoResetEvent mdResetEvent = new AutoResetEvent(false);

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.Login", Description = "Login to API")]
    public static object Login()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null && mdApi != null) return "[CTP-Future] API is ready.";

      if (api == null)
      {
        if (!LoginTraderApi()) return "#ERR: [CTP-Future] failed to login Trade API: " + errorText;
      }

      if (mdApi == null)
      {
        if (!LoginMdApi()) return "#ERR: [CTP-Future] failed to login Quote API: " + mdErrorText;
      }

      return "[CTP-Future] API is ready.";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.Logout", Description = "Log out API")]
    public static object Logout()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null)
      {
        var logoutField = new CThostFtdcUserLogoutField
        {
          BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
          UserID = Settings.Default.CTP_Future_Trade_UserID
        };

        api.ReqUserLogout(logoutField, 1);
        api.Release();
        api = null;
      }

      if (mdApi != null)
      {
        var logoutField = new CThostFtdcUserLogoutField
        {
          BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
          UserID = Settings.Default.CTP_Future_Trade_UserID
        };

        mdApi.ReqUserLogout(logoutField, 1);
        mdApi.Release();
        mdApi = null;
      }

      return "[CTP-Future] API is logged out.";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.TraderApiConnected",
      Description = "Check trader API connection")]
    public static object TraderApiConnected()
    {
      lock (isTraderApiConnected) return isTraderApiConnected;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.MdApiConnected",
      Description = "Check marketdata API connection")]
    public static object MdApiConnected()
    {
      lock (isMdApiConnected) return isMdApiConnected;
    }

    private static bool LoginTraderApi()
    {
      // Create and intialize trader API
      api = CThostFtdcTraderApi.CreateFtdcTraderApi(Path.GetTempPath());
      callback = new CThostFtdcTraderSpiEventCallback();
      api.RegisterSpi(callback);
      api.SubscribePrivateTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.SubscribePublicTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.RegisterFront(Settings.Default.CTP_Future_Trade_Address);
      api.Init();

      // Internal housekeeping function
      void cleanup()
      {
        if (api != null) api.Release();
        api = null;
      }

      // OnFrontConnected event handler
      callback.OnFrontConnectedEvent += () =>
      {
        resetEvent.Set();
      };

      // OnFrontDisconnected event handler
      callback.OnFrontDisconnectedEvent += (reason) =>
      {
        errorText = "disconnected: reason=" + reason;
        lock(isTraderApiConnected) isTraderApiConnected = false;
        resetEvent.Set();
        cleanup();
      };
 
      // OnRspAuthenticate event handler
      callback.OnRspAuthenticateEvent += (pRspAuthenticateField, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID != 0)
          errorText = "authentication failed: error=" + pRspInfo.ErrorID;

        resetEvent.Set();
      };

      // OnRspUserLogin event handler
      callback.OnRspUserLoginEvent += (pRspUserLogin, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID != 0)
          errorText = "login failed: error=" + pRspInfo.ErrorID;

        resetEvent.Set();
      };

      // Wait for OnFrontConnected
      resetEvent.WaitOne();

      // Authentication
      var auth = new CThostFtdcReqAuthenticateField
      {
        BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
        UserID = Settings.Default.CTP_Future_Trade_UserID,
        AuthCode = Settings.Default.CTP_Future_Trade_AuthCode,
        AppID = Settings.Default.CTP_Future_Trade_AppID
      };

      api.ReqAuthenticate(auth, 1);
      resetEvent.WaitOne(); // wait for OnRspAuthenticate

      // Login
      var loginAuth = new CThostFtdcReqUserLoginField
      {
        BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
        UserID = Settings.Default.CTP_Future_Trade_UserID,
        Password = Settings.Default.CTP_Future_Trade_Password
      };

      api.ReqUserLogin(loginAuth, 1);
      resetEvent.WaitOne(); // wait for OnRspUserLogin

      if (errorText == "")
      {
        isTraderApiConnected = true;
        return true;
      }

      api.Release();
      api = null;
      return false;
    }

    private static bool LoginMdApi()
    {
      // Create and intialize marketdata API
      mdApi = CThostFtdcMdApi.CreateFtdcMdApi(Path.GetTempPath());
      mdCallback = new CThostFtdcMdSpiEventCallback();
      mdApi.RegisterSpi(mdCallback);
      mdApi.RegisterFront(Settings.Default.CTP_Future_MD_Address);
      mdApi.Init();

      // Internal housekeeping function
      void cleanup()
      {
        if (mdApi != null) mdApi.Release();
        mdApi = null;
      }

      // OnFrontConnected event handler
      mdCallback.OnFrontConnectedEvent += () =>
      {
        mdResetEvent.Set();
      };

      // OnFrontDisconnected event handler
      mdCallback.OnFrontDisconnectedEvent += (reason) =>
      {
        mdErrorText = "disconnected: reason=" + reason;
        lock (isMdApiConnected) isMdApiConnected = false;
        mdResetEvent.Set();
        cleanup();
      };

      // OnRspUserLogin event handler
      mdCallback.OnRspUserLoginEvent += (pRspUserLogin, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID != 0)
          mdErrorText = "login failed: error=" + pRspInfo.ErrorID;

        mdResetEvent.Set();
      };

      // Wait for OnFrontConnected
      mdResetEvent.WaitOne();

      // Login
      var loginAuth = new CThostFtdcReqUserLoginField
      {
        BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
        UserID = Settings.Default.CTP_Future_Trade_UserID,
        Password = Settings.Default.CTP_Future_Trade_Password
      };

      mdApi.ReqUserLogin(loginAuth, 1);
      mdResetEvent.WaitOne(); // wait for OnRspUserLogin

      if (mdErrorText == "")
      {
        isMdApiConnected = true;
        return true;
      }

      mdApi.Release();
      mdApi = null;
      return false;
    }
  }

  public static class APIFunctions
  {
    public static readonly string[] scalarFields = new string[] { "UpdateTime", "LastPrice", "Volume" };
    public static readonly string[] vectorFields = new string[] { "BidPrice", "BidVolume", "AskPrice", "AskVolume" };
    public static readonly int marketDepth = 5;
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
        for (var j = 1; j <= marketDepth; ++j)
        {
          result.Add(vectorFields[i] + j);
        }
      }

      return result.ToArray();
    }

    private static bool orderEventHanldersInitialized = false;

    #region Event Handlers
    // Handler of OnRspOrderInsert event
    private static void OnRspOrderInsertHandler(CThostFtdcInputOrderField pInputOrder, CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
    {
      if (pRspInfo.ErrorID != 0)
      {
        var msg = "#ERR: [CTP-Future] Failed to send order: error=" + pRspInfo.ErrorID;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }

    // Handler of OnErrRtnOrderInsert event
    private static void OnErrRtnOrderInsertHandler(CThostFtdcInputOrderField pInputOrder, CThostFtdcRspInfoField pRspInfo)
    {
      if (pRspInfo.ErrorID != 0)
      {
        var msg = "#ERR: [CTP-Future] Failed to send order: error=" + pRspInfo.ErrorID;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }

    // Handler of OnRtnOrder event
    private static void OnRtnOrderHandler(CThostFtdcOrderField pOrder)
    {
      if (pOrder.OrderSysID != "")
      {
        var msg = "#INFO: [CTP-Future] Order sent to exchange successfully: orderSysId=" + pOrder.OrderSysID;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }

    // Handler of OnRspOrderAction event
    private static void OnRspOrderActionHandler(CThostFtdcInputOrderActionField pInputOrderAction, CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
    {
      if (pRspInfo.ErrorID != 0)
      {
        var msg = "#ERR: [CTP-Future] Failed to cancel order: error=" + pRspInfo.ErrorID;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }

    // Handler of OnRspOrderAction event
    private static void OnErrRtnOrderActionHandler(CThostFtdcOrderActionField pOrderAction, CThostFtdcRspInfoField pRspInfo)
    {
      if (pRspInfo.ErrorID != 0)
      {
        var msg = "#ERR: [CTP-Future] Failed to cancel order: error=" + pRspInfo.ErrorID;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }
    #endregion

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.SendOrder", Description = "Send order")]
    public static object SendOrder(string exchange, string instrumentId, bool isBuy,
      bool isOpen, bool isCloseToday, double price, int quantity)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: [CTP-Future] Trade API not ready yet.";

      if (!orderEventHanldersInitialized)
      {
        APIInstance.callback.OnRspOrderInsertEvent += OnRspOrderInsertHandler;
        APIInstance.callback.OnErrRtnOrderInsertEvent += OnErrRtnOrderInsertHandler;
        APIInstance.callback.OnRspOrderActionEvent += OnRspOrderActionHandler;
        APIInstance.callback.OnErrRtnOrderActionEvent += OnErrRtnOrderActionHandler;

        orderEventHanldersInitialized = true;
      }

      char combOffsetFlag = isOpen ? External.THOST_FTDC_OF_Open
        : (isCloseToday ? External.THOST_FTDC_OF_CloseToday : External.THOST_FTDC_OF_Close);

      var newOrder = new CThostFtdcInputOrderField
      {
        ExchangeID = exchange,
        InstrumentID = instrumentId,
        LimitPrice = price,
        VolumeTotalOriginal = quantity,
        Direction = isBuy ? External.THOST_FTDC_D_Buy : External.THOST_FTDC_D_Sell,

        BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
        InvestorID = Settings.Default.CTP_Future_Trade_UserID,
        UserID = Settings.Default.CTP_Future_Trade_UserID,
        OrderPriceType = External.THOST_FTDC_OPT_LimitPrice,
        CombOffsetFlag = combOffsetFlag.ToString(),

        // Mandatory but irrelevant fields
        CombHedgeFlag = External.THOST_FTDC_HF_Speculation.ToString(),
        ContingentCondition = External.THOST_FTDC_CC_Immediately,
        ForceCloseReason = External.THOST_FTDC_FCC_NotForceClose,
        VolumeCondition = External.THOST_FTDC_VC_AV,
        TimeCondition = External.THOST_FTDC_TC_GFD,
      };

      var errorCode = APIInstance.api.ReqOrderInsert(newOrder, 1);

      if (errorCode == 0)
        return "[CTP-Future] Send order: success!";
      else
        return "#ERR: [CTP-Future] Failed to send order: error=" + errorCode;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.CancelOrder", Description = "Cancel order")]
    public static object CancelOrder(string exchange, string instrumentId, string orderId)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: [CTP-Future] Trade API not ready yet.";

      var orderCancelReq = new CThostFtdcInputOrderActionField
      {
        ExchangeID = exchange,
        InstrumentID = instrumentId,
        OrderSysID = orderId.PadLeft(12),
        ActionFlag = External.THOST_FTDC_AF_Delete,

        BrokerID = Settings.Default.CTP_Future_Trade_BrokerID,
        InvestorID = Settings.Default.CTP_Future_Trade_UserID,
        UserID = Settings.Default.CTP_Future_Trade_UserID,
      };

      var errorCode = APIInstance.api.ReqOrderAction(orderCancelReq, 1);

      if (errorCode == 0)
        return "[CTP-Future] Cancel order: success!";
      else
        return "#ERR: [CTP-Future] Failed to cancel order: error=" + errorCode;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.GetQuoteDetails", Description = "Get quote details")]
    public static object GetQuoteDetails(string instrumentId, string field)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.mdApi == null) return "#ERR: [CTP-Future] Quote API not ready yet.";

      if (Array.IndexOf(allFields, field) == -1) return "#ERR: [CTP-Future] Invalid quote field.";

      return XlCall.RTD(ApiRtdServer.ServerProgId, null, new string[] { instrumentId, field });
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.CtpFuture.GetQuoteFields", Description = "Get supported quote fields")]
    public static object GetQuoteFields()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.mdApi == null) return "#ERR: [CTP-Future] Quote API not ready yet.";

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
    public const string ServerProgId = "ApiRtdServer.CtpFuture";

    private Dictionary<string, Topic> topicMap; // contractCode + field => topic
    private Dictionary<string, List<object>> quoteMap; // contractCode => list of quote values
    private static List<string> subscribedInstruments = new List<string>(); // for re-subscriptions on API re-connection

    public static void ReSubscribe()
    {
      lock (subscribedInstruments)
      {
        APIInstance.mdApi.SubscribeMarketData(subscribedInstruments.ToArray(), subscribedInstruments.Count);
      }
    }

    protected override bool ServerStart()
    {
      topicMap = new Dictionary<string, Topic>();
      quoteMap = new Dictionary<string, List<object>>();

      APIInstance.mdCallback.OnRspSubMarketDataEvent += (pSpecificInstrument, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID != 0)
        {
          var msg = "#ERR: [CTP-Future] OnRspSubMarketData event failed: " + pSpecificInstrument.InstrumentID
            + ", error=" + pRspInfo.ErrorID;

          new Thread(() => MessageBox.Show(msg)).Start();
        }
      };

      APIInstance.mdCallback.OnRtnDepthMarketDataEvent += (pDepthMarketData) =>
      {
        var instrumentId = pDepthMarketData.ExchangeID + pDepthMarketData.InstrumentID;

        var quoteInfo = new List<object>();

        foreach (var field in APIFunctions.allFields)
        {
          var value = pDepthMarketData.GetType().GetProperty(field).GetValue(pDepthMarketData);
          quoteInfo.Add(value);

          var topicKey = instrumentId + field;
          if (topicMap.ContainsKey(topicKey))
            topicMap[topicKey].UpdateValue(value);
        }

        quoteMap[instrumentId] = quoteInfo;
      };

      return true;
    }

    protected override void ServerTerminate()
    {
      topicMap.Clear();
      quoteMap.Clear();
    }

    protected override object ConnectData(Topic topic, IList<string> topicInfo, ref bool newValues)
    {
      var instrumentId = topicInfo[0];
      var field = topicInfo[1];
      var topicKey = instrumentId + field;

      if (!topicMap.ContainsKey(topicKey))
        topicMap.Add(topicKey, topic);

      if (quoteMap.ContainsKey(instrumentId))
      {
        var fieldIndex = Array.IndexOf(APIFunctions.allFields, field);
        var value = quoteMap[instrumentId][fieldIndex];

        topicMap[topicKey].UpdateValue(value);
        return value;
      }
      else
      {
        var errorCode = APIInstance.mdApi.SubscribeMarketData(new string[] { instrumentId }, 1);

        if (errorCode != 0)
        {
            var msg = "#ERR: [CTP-Future] Quote RTD failed for [" + instrumentId + "]: error=" + errorCode;
            new Thread(() => MessageBox.Show(msg)).Start();
        }
        else
        {
          lock (subscribedInstruments)
            subscribedInstruments.Add(instrumentId);
        }

        return 0.0;
      }
    }
  }
}
