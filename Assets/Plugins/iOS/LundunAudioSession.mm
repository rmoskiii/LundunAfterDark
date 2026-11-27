// G8.2a: the car radio's audio-session observer (proposal 4.1c / 4.1d). Called from LundunRadio.cs (iOS device builds
// only). It keeps the interruption state itself and tells the radio through UnitySendMessage("LundunRadio",
// "OnAudioSessionEvent", "sequence|event|detail"), events: began, resume, noresume, activationfailed.
//
//   iOS 27 and later (built with the iOS 27 SDK): lifecycle notifications only.
//     DidBecomeInactive with a DeactivationContext whose source is the system  → Interrupted ("began")
//     DidBecomeInactive with source = the app (Unity or the app deactivated)   → ignored (logged only)
//     ResumptionRecommendation = should resume                                  → ResumeRecommended, reactivate
//     ResumptionRecommendation = should not resume                              → DoNotResume ("noresume")
//     DidBecomeActive: confirmation only (from ResumeRecommended → Active, "resume"); never permission on its own
//   iOS 26 and earlier (or an older SDK): the legacy interruption notification only (Began / Ended ± ShouldResume).
//   UIApplicationDidBecomeActive: a retry point only. Never overrides DoNotResume.
//
// The ONLY thing it changes: after a should-resume recommendation (or the player's own action) it reactivates the audio
// session with setActive:YES. It never changes the category, mode or options, and never reactivates after should-not-
// resume. Observers are added once per app process (dispatch_once), hold no Unity object, and live as long as the app.
//
// Values from the iOS 27 API references (read through KVC so only the public notification and key names are needed):
//   AVAudioSessionDeactivationSource: App = 1, System = 2
//   AVAudioSessionResumptionRecommendation: ShouldNotResume = 0, ShouldResume = 1
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

enum { LASActive = 0, LASInterrupted = 1, LASResumeRecommended = 2, LASDoNotResume = 3 };

static int lasState = LASActive;
static int lasSequence = 0;
static BOOL lasLegacy = YES;                       // which path this device uses (decided once)
static NSString* lasTarget = @"LundunRadio";
static NSMutableArray* lasObservers = nil;         // kept for the life of the process

static void LASSend(NSString* event, NSString* detail)
{
    lasSequence++;
    NSString* msg = [NSString stringWithFormat:@"%d|%@|%@", lasSequence, event, detail ?: @""];
    UnitySendMessage([lasTarget UTF8String], "OnAudioSessionEvent", [msg UTF8String]);
}

static void LASLog(NSString* s) { NSLog(@"[Radio] native: %@", s); }

/// Reactivates the session (no category / mode / option change). On success: Active, and the radio is told to resume.
static void LASReactivate(NSString* why)
{
    NSError* err = nil;
    BOOL ok = [[AVAudioSession sharedInstance] setActive:YES error:&err];
    if (ok)
    {
        lasState = LASActive;
        LASLog([NSString stringWithFormat:@"reactivated (%@)", why]);
        LASSend(@"resume", why);
    }
    else
    {
        LASLog([NSString stringWithFormat:@"reactivation failed (%@): %ld", why, (long)err.code]);
        LASSend(@"activationfailed", [NSString stringWithFormat:@"%ld", (long)err.code]);
    }
}

static void LASAddObserver(NSString* name, void (^block)(NSNotification*))
{
    id o = [[NSNotificationCenter defaultCenter] addObserverForName:name object:nil queue:[NSOperationQueue mainQueue] usingBlock:block];
    [lasObservers addObject:o];
}

static void LASRegisterLegacy()
{
    LASAddObserver(AVAudioSessionInterruptionNotification, ^(NSNotification* n) {
        NSNumber* type = n.userInfo[AVAudioSessionInterruptionTypeKey];
        if (type == nil) return;
        if (type.unsignedIntegerValue == AVAudioSessionInterruptionTypeBegan)
        {
            lasState = LASInterrupted;
            LASLog(@"legacy: interruption began");
            LASSend(@"began", @"legacy");
        }
        else
        {
            NSNumber* opts = n.userInfo[AVAudioSessionInterruptionOptionKey];
            BOOL resume = opts != nil && (opts.unsignedIntegerValue & AVAudioSessionInterruptionOptionShouldResume) != 0;
            if (resume) { lasState = LASResumeRecommended; LASLog(@"legacy: ended, should resume"); LASReactivate(@"shouldResume"); }
            else { lasState = LASDoNotResume; LASLog(@"legacy: ended, should not resume"); LASSend(@"noresume", @"shouldNotResume"); }
        }
    });
}

#if defined(__IPHONE_27_0) && __IPHONE_OS_VERSION_MAX_ALLOWED >= __IPHONE_27_0
static void LASRegisterLifecycle() API_AVAILABLE(ios(27.0))
{
    LASAddObserver(AVAudioSessionDidBecomeInactiveNotification, ^(NSNotification* n) {
        id ctx = n.userInfo[AVAudioSessionDeactivationContextKey];
        NSInteger source = 0;
        @try { if (ctx != nil) source = [[ctx valueForKey:@"source"] integerValue]; } @catch (NSException* e) { source = 0; }
        if (source == 2)                                              // the system
        {
            lasState = LASInterrupted;
            id ic = nil;
            @try { ic = [ctx valueForKey:@"interruptionContext"]; } @catch (NSException* e) { ic = nil; }
            LASLog([NSString stringWithFormat:@"inactive (system): %@", ic ?: @"-"]);
            LASSend(@"began", @"system");
        }
        else LASLog([NSString stringWithFormat:@"inactive (source %ld, app-initiated): not an interruption", (long)source]);
    });
    LASAddObserver(AVAudioSessionResumptionRecommendationNotification, ^(NSNotification* n) {
        id ctx = n.userInfo[AVAudioSessionResumptionContextKey];
        NSInteger rec = 0;
        @try { if (ctx != nil) rec = [[ctx valueForKey:@"recommendation"] integerValue]; } @catch (NSException* e) { rec = 0; }
        if (rec == 1)                                                 // should resume
        {
            lasState = LASResumeRecommended;
            LASLog(@"recommendation: should resume");
            LASReactivate(@"shouldResume");
        }
        else
        {
            lasState = LASDoNotResume;
            LASLog(@"recommendation: should not resume");
            LASSend(@"noresume", @"shouldNotResume");
        }
    });
    LASAddObserver(AVAudioSessionDidBecomeActiveNotification, ^(NSNotification* n) {
        if (lasState == LASResumeRecommended)
        {
            lasState = LASActive;
            LASLog(@"active (confirmation)");
            LASSend(@"resume", @"confirmed");
        }
        else LASLog([NSString stringWithFormat:@"active notification in state %d: confirmation only, nothing changed", lasState]);
    });
}
#endif

extern "C" void LundunAudioSession_Start(const char* unityObject)
{
    if (unityObject != NULL) lasTarget = [NSString stringWithUTF8String:unityObject];   // later calls only update the name
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        lasObservers = [NSMutableArray array];
        lasLegacy = YES;
#if defined(__IPHONE_27_0) && __IPHONE_OS_VERSION_MAX_ALLOWED >= __IPHONE_27_0
        if (@available(iOS 27.0, *)) { lasLegacy = NO; LASRegisterLifecycle(); }
#endif
        if (lasLegacy) LASRegisterLegacy();
        LASAddObserver(UIApplicationDidBecomeActiveNotification, ^(NSNotification* n) {
            if (lasState == LASResumeRecommended) LASReactivate(@"appActiveRetry");          // one retry after a failed reactivation
            else if (lasState == LASInterrupted && lasLegacy) { lasState = LASResumeRecommended; LASReactivate(@"appActiveNoEnd"); }   // legacy: an end iOS never reported
            else if (lasState == LASDoNotResume) LASLog(@"app active: should-not-resume stands (waiting for the player)");
        });
        LASLog(lasLegacy ? @"observing: legacy interruption path" : @"observing: iOS 27 lifecycle path");
    });
}

extern "C" int LundunAudioSession_State() { return lasState; }
extern "C" int LundunAudioSession_Sequence() { return lasSequence; }

/// The player's own action (tapping the pill, changing RADIO, getting into a car) after should-not-resume.
extern "C" void LundunAudioSession_UserResume()
{
    if (lasState != LASDoNotResume) return;
    lasState = LASResumeRecommended;
    LASReactivate(@"player");
}
