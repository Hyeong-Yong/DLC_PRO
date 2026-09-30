// 통신 라이브러리(DlcPro.Core) 통합 테스트 — 먼저 DlcSim 시뮬레이터를 실행한 뒤 실행하세요.
// 실행: dotnet run --project tools/SimTest   (실제 장비에는 절대 실행하지 마세요: 증폭기 전류를 바꿉니다)
using System; using System.Threading; using DLC_PRO.Core;
class SyncProgress : IProgress<double> { readonly Action<double> a; public SyncProgress(Action<double> a){this.a=a;} public void Report(double v){a(v);} }
class M{
 static int fails=0;
 static void Check(bool c,string m){ Console.WriteLine((c?"PASS ":"FAIL ")+m); if(!c) fails++; }
 public static void Run(int cmdPort, int monPort){
  var d=new DlcDevice();
  d.LogMessage+=(l,m)=>Console.WriteLine("  ["+l+"] "+m);
  d.WatchMany(100,P.DlCcCurrentAct,P.LockState,P.LockStateTxt,P.AmpSeedPower,P.LaserType,P.ScopeUpdateRate,"laser1:does-not-exist");
  d.ConnectTcp("127.0.0.1", cmdPort, monPort);
  Thread.Sleep(800);
  Check(d.MonitorAvailable,"monitor available");
  Check(d.GetString(P.LaserType)=="TApro","laser type via monitor = "+d.GetString(P.LaserType));
  Check(d.IsUnavailable("laser1:does-not-exist"),"unknown param flagged unavailable");
  int code=d.SetAsync(P.DlCcCurrentSet,95.25).Result; Check(code==0,"param-set ok");
  code=d.SetAsync(P.DlCcCurrentSet,500.0).Result; Check(code==2,"clip warning code 2");
  Check(Math.Abs(d.GetDouble(P.DlCcCurrentSet,0)-106)<1e-6,"cache updated after set to clip value");
  try{ d.SetAsync(P.DlCcCurrentAct,1.0).Wait(); Check(false,"read-only should fail"); }catch(AggregateException ex){ Check(ex.GetBaseException() is UnauthorizedAccessException,"read-only rejected locally: "+ex.GetBaseException().Message); }
  d.SetAsync(P.DlCcCurrentSet,94.5).Wait();
  int frames=0; ScopeFrame last=null; d.ScopeFrameReceived+=f=>{frames++; last=f;};
  d.ScopeStreaming=true; Thread.Sleep(1200);
  Check(frames>=5,"scope frames: "+frames);
  Check(last!=null&&last.X!=null&&last.X.Length==1000&&last.Y1.Length==1000,"scope blob parsed x/y 1000 pts");
  d.ExecAsync(P.CmdLockFindCandidates).Wait(); Thread.Sleep(500);
  Check(d.GetInt(P.LockState,-1)==2,"state selecting");
  Check(last.Candidates!=null&&last.Candidates.Length>0,"candidates: "+(last.Candidates==null?0:last.Candidates.Length));
  var c0=last.Candidates[last.Candidates.Length/2];
  d.ExecAsync(P.CmdLockSelectLockpoint,(double)c0.X,(double)c0.Y,0).Wait(); Thread.Sleep(400);
  Check(d.GetInt(P.LockState,-1)==3,"state selected");
  Check(last.LockPoint.HasValue,"lockpoint in blob: "+(last.LockPoint.HasValue?last.LockPoint.Value.X.ToString():"none"));
  d.ExecAsync(P.CmdLockClose).Wait(); Thread.Sleep(900);
  Check(d.GetInt(P.LockState,-1)==5,"state locked ("+d.GetString(P.LockStateTxt)+")");
  Check(last.BackgroundX!=null,"background trace fetched");
  d.ExecAsync(P.CmdLockOpen).Wait(); Thread.Sleep(400);
  Check(d.GetInt(P.LockState,-1)==3,"unlocked → selected");
  try{ d.ExecAsync("laser1:bogus").Wait(); Check(false,"bogus exec"); }catch(AggregateException ex){ Check(ex.GetBaseException() is UnauthorizedAccessException,"unknown exec rejected locally: "+ex.GetBaseException().Message); }
  // recorder
  d.SetAsync(P.RecRecordingTime,300.0).Wait(); d.SetAsync(P.RecEnabled,true).Wait(); Thread.Sleep(700);
  var rd=d.RunAsync(cl=>RecorderData.Fetch(cl,null)).Result;
  Check(rd.Count==5000&&rd.Y1.Length==5000,"recorder fetch "+rd.Count+" samples, xname="+rd.XName);
  string raw=d.SendRawAsync("(param-ref 'laser1:type)").Result; Check(raw.Trim()=="\"TApro\"","raw console: "+raw);
  // safety
  var s=new AmpSafety(d,new AmpSafetySettings{RampStepMa=500,RampIntervalMs=50});
  Thread.Sleep(600);
  Check(s.HasAmplifier,"has amplifier");
  Check(s.CheckEnableAmp()==null,"amp enable allowed: "+(s.CheckEnableAmp()??"ok"));
  d.SetAsync(P.AmpCcCurrentSet,1000.0).Wait(); d.SetAsync(P.AmpCcEnabled,true).Wait(); Thread.Sleep(300);
  s.SetAmpCurrentAsync(2500).Wait(); Check(Math.Abs(d.GetDouble(P.AmpCcCurrentSet,0)-2500)<1e-6,"ramped to 2500");
  try{ s.SetAmpCurrentAsync(5000).Wait(); Check(false,"over max"); }catch(AggregateException){ Check(true,"over max rejected"); }
  string trip=null; s.Tripped+=r=>trip=r;
  d.SetAsync(P.DlCcCurrentSet,50.0).Wait(); Thread.Sleep(1200); // seed drops below 10 mW
  Check(trip!=null,"watchdog tripped: "+trip);
  Check(d.GetBool(P.AmpCcEnabled,true)==false,"amp disabled by watchdog");
  d.SetAsync(P.DlCcCurrentSet,94.5).Wait(); Thread.Sleep(800);
  // 증폭기 ON 시퀀스: 한 스텝(100 mA)에서 시작해 설정값까지 램프
  var steps=new System.Collections.Generic.List<double>();
  d.SetAsync(P.AmpCcCurrentSet,1000.0).Wait();
  s.EnableAmpAsync(new SyncProgress(v=>steps.Add(v))).Wait();
  Check(steps.Count>=2 && steps[0]<=500.0+1e-6 && Math.Abs(steps[steps.Count-1]-1000)<1e-6,"EnableAmpAsync ramp "+string.Join(",",steps));
  Check(d.GetBool(P.AmpCcEnabled,false),"amp on after EnableAmpAsync");
  // ALL OFF: 증폭기 → 마스터
  s.AllOffAsync().Wait(); Thread.Sleep(300);
  Check(!d.GetBool(P.AmpCcEnabled,true) && !d.GetBool(P.DlCcEnabled,true),"AllOffAsync: amp & master off");
  d.SetAsync(P.DlCcEnabled,true).Wait();
  // 레코더 청크 읽기 (작업 큐를 1024점마다 양보)
  var rd2=RecorderData.FetchAsync(d,null).Result;
  Check(rd2.Count==5000 && !rd2.Incomplete,"FetchAsync "+rd2.Count);
  // 여러 줄 콘솔 명령은 한 줄로 합쳐 전송
  Check(d.SendRawAsync("(param-ref\n 'laser1:type)").Result.Trim()=="\"TApro\"","multi-line console joined");
  // 줄바꿈이 든 문자열 값은 거부
  try{ d.SetAsync(P.LaserLabel,"a\nb").Wait(); Check(false,"newline string"); }catch(AggregateException ex){ Check(ex.GetBaseException() is ArgumentException,"newline in string rejected"); }
  // await 시 원래 예외 형식 (AggregateException 아님)
  try{ d.SetAsync(P.DlCcCurrentAct,1.0).GetAwaiter().GetResult(); }catch(Exception ex){ Check(ex is UnauthorizedAccessException,"awaited exception type "+ex.GetType().Name); }
  s.Dispose();
  // 빠른 재접속 반복
  for(int k=0;k<3;k++){ d.Disconnect(); d.ConnectTcp("127.0.0.1", cmdPort, monPort); }
  Thread.Sleep(800);
  Check(d.IsConnected && d.GetString(P.LaserType)=="TApro","reconnect x3 ok");
  d.Disconnect();
  if (fails != 0) throw new Exception("Legacy integration failures: " + fails);
 }}
