using System;
using System.IO;
using System.Threading;
using CTP.Future;

namespace APITester
{
  class Program
  {
    static void Main(string[] args)
    {
      // Create and intialize trader API
      var api = CThostFtdcTraderApi.CreateFtdcTraderApi(Path.GetTempPath());
      Console.WriteLine("CTP version: {0}", CThostFtdcTraderApi.GetApiVersion());

      var resetEvent = new AutoResetEvent(false);

      var callback = new CThostFtdcTraderSpiEventCallback();
      api.RegisterSpi(callback);

      api.SubscribePrivateTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.SubscribePublicTopic(THOST_TE_RESUME_TYPE.THOST_TERT_QUICK);
      api.RegisterFront("tcp://115.238.106.252:41207");
      api.Init();

      callback.OnFrontConnectedEvent += () =>
      {
        Console.WriteLine("CTP connected");
        resetEvent.Set();
      };

      callback.OnHeartBeatWarningEvent += (nTimeLapse) =>
      {
        Console.WriteLine("CTP heartbeat warning: {0}", nTimeLapse);
      };

      // OnFrontDisconnected event handler
      callback.OnFrontDisconnectedEvent += (reason) =>
      {
        Console.WriteLine("CTP disconnected: reason={0}", reason);
        resetEvent.Set();
      };

      // OnRspAuthenticate event handler
      callback.OnRspAuthenticateEvent += (pRspAuthenticateField, pRspInfo, nRequestID, bIsLast) =>
      {
        Console.WriteLine("CTP OnRspAuthenticate error: {0}, {1}", pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        resetEvent.Set();
      };

      // OnRspUserLogin event handler
      callback.OnRspUserLoginEvent += (pRspUserLogin, pRspInfo, nRequestID, bIsLast) =>
      {
        Console.WriteLine("CTP OnRspUserLogin error: {0}, {1}", pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        resetEvent.Set();
      };

      // Wait for OnFrontConnected
      resetEvent.WaitOne();

      // Authentication
      var auth = new CThostFtdcReqAuthenticateField
      {
        BrokerID = "1008",
        UserID = "90100951",
        AuthCode = "WK1JU573429UXGLM",
        AppID = "client_PureArb_1.0.0"
      };

      var returnCode = api.ReqAuthenticate(auth, 1);
      Console.WriteLine("CTP ReqAuthenticate return code: {0}", returnCode);

      resetEvent.WaitOne(); // wait for OnRspAuthenticate

      // Login
      var loginAuth = new CThostFtdcReqUserLoginField
      {
        BrokerID = "1008",
        UserID = "90100951",
        Password = "abc9978217"
      };

      returnCode = api.ReqUserLogin(loginAuth, 1);
      Console.WriteLine("CTP ReqUserLogin return code: {0}", returnCode);
      resetEvent.WaitOne(); // wait for OnRspUserLogin

      callback.OnRspOrderInsertEvent += (pInputOrder, pRspInfo, nRequestID, bIsLast) =>
      {
        Console.WriteLine("CTP OnRspOrderInsert error: {0}, {1}", pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        resetEvent.Set();
      };

      callback.OnErrRtnOrderInsertEvent += (pInputOrder, pRspInfo) =>
      {
        Console.WriteLine("CTP OnErrRtnOrderInsert error: {0}, {1}", pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        resetEvent.Set();
      };

      var newOrder = new CThostFtdcInputOrderField
      {
        ExchangeID = "SSE",
        BrokerID = "1008",
        InvestorID = "90100951",
        InstrumentID = "IF2012",
        UserID = "90100951",
        OrderPriceType = External.THOST_FTDC_OPT_LimitPrice,
        Direction = External.THOST_FTDC_D_Buy,
        CombOffsetFlag = External.THOST_FTDC_OF_Open.ToString(),
        CombHedgeFlag = External.THOST_FTDC_HF_Speculation.ToString(),
        ContingentCondition = External.THOST_FTDC_CC_Immediately,
        ForceCloseReason = External.THOST_FTDC_FCC_NotForceClose,
        VolumeCondition = External.THOST_FTDC_VC_AV,
        TimeCondition = External.THOST_FTDC_TC_GFD,
        LimitPrice = 4669,
        VolumeTotalOriginal = 10
      };

      var errorCode = api.ReqOrderInsert(newOrder, 1);
      Console.WriteLine("CTP OrderInsert error code: {0}", errorCode);

      resetEvent.WaitOne();
      resetEvent.WaitOne();
    }
  }
}
