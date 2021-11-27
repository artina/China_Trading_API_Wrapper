using System.IO;
using System.Threading;
using ExcelDna.Integration;
using TradingAddin.Properties;

namespace CTP.Stock
{
  // Contains the API singleton
  public static class APIInstance
  {
    public static CThostFtdcTraderApi api = null;
    public static CThostFtdcTraderSpiEventCallback callback = null;

    private static string errorText = "";
    private static ManualResetEvent resetEvent = new ManualResetEvent(false);

    public static object Login()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null) return "API is ready.";

      // Create and intialize API
      api = CThostFtdcTraderApi.CreateFtdcTraderApi(Path.GetTempPath());
      callback = new CThostFtdcTraderSpiEventCallback();
      api.RegisterSpi(callback);
      api.SubscribePrivateTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.SubscribePublicTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.RegisterFront(Settings.Default.CTP_Stock_Trade_Address);
      api.Init();

      // OnRspAuthenticate event handler
      callback.OnRspAuthenticateEvent += (pRspAuthenticateField, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID != 0)
        {
          errorText = "#ERR: Failed to authenticate: error code is " + pRspInfo.ErrorID;
          resetEvent.Set();
        }
      };

      // OnRspUserLogin event handler
      callback.OnRspUserLoginEvent += (pRspUserLogin, pRspInfo, nRequestID, bIsLast) =>
      {
        if (pRspInfo.ErrorID == 0)
          resetEvent.Set();
        else
        {
          errorText = "#ERR: Failed to login: error code is " + pRspInfo.ErrorID;
          resetEvent.Set();
        }
      };

      // Authentication
      var auth = new CThostFtdcReqAuthenticateField
      {
        BrokerID = Settings.Default.CTP_Stock_Trade_BrokerID,
        UserID = Settings.Default.CTP_Stock_Trade_UserID,
        AuthCode = Settings.Default.CTP_Stock_Trade_AuthCode,
        AppID = Settings.Default.CTP_Stock_Trade_AppID
      };
      var errorCode = api.ReqAuthenticate(auth, 1);
      if (errorCode != 0)
        return "#ERR: Failed to authenticate: error code is " + errorCode;

      // Login
      var loginAuth = new CThostFtdcReqUserLoginField
      {
        BrokerID = Settings.Default.CTP_Stock_Trade_BrokerID,
        UserID = Settings.Default.CTP_Stock_Trade_UserID,
        Password = Settings.Default.CTP_Stock_Trade_Password
      };
      errorCode = api.ReqUserLogin(loginAuth, 1);
      if (errorCode != 0)
        return "#ERR: Failed to login: error code is " + errorCode;

      resetEvent.WaitOne();

      if (errorText == "")
        return "API is ready.";

      api = null;
      return errorText;
    }
  }

  public static class APIFunctions
  {
    public static object SendOrder(string exchange, string instrumentId, bool isBuyOrder, string openCoverFlag, double price, int quantity)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      var newOrder = new CThostFtdcInputOrderField
      {
        ExchangeID = exchange,
        BrokerID = Settings.Default.CTP_Stock_Trade_BrokerID,
        InvestorID = Settings.Default.CTP_Stock_Trade_UserID,
        InstrumentID = instrumentId,
        UserID = Settings.Default.CTP_Stock_Trade_UserID,
        OrderPriceType = External.THOST_FTDC_OPT_LimitPrice,
        Direction = isBuyOrder ? External.THOST_FTDC_D_Buy : External.THOST_FTDC_D_Sell,
        CombOffsetFlag = openCoverFlag,
        LimitPrice = price,
        VolumeTotalOriginal = quantity
      };

      var errorCode = APIInstance.api.ReqOrderInsert(newOrder, 1);

      if (errorCode == 0)
        return "Success!";
      else
        return "#ERR: Failed to send order: error code is " + errorCode;
    }

    public static object CancelOrder(string orderNo)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      var orderCancelReq = new CThostFtdcInputOrderActionField
      {
        OrderSysID = orderNo,
        ActionFlag = External.THOST_FTDC_AF_Delete
      };

      var errorCode = APIInstance.api.ReqOrderAction(orderCancelReq, 1);

      if (errorCode == 0)
        return "Success!";
      else
        return "#ERR: Failed to cancel order: error code is " + errorCode;
    }
  }
}
