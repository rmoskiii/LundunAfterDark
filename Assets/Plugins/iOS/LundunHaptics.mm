// Lundun haptics: Apple's light / medium / heavy "tap" feedback, called from LundunHaptics.cs.
#import <UIKit/UIKit.h>

extern "C" void LundunHapticImpact(int style)
{
    UIImpactFeedbackStyle s = style <= 0 ? UIImpactFeedbackStyleLight
                            : style == 1 ? UIImpactFeedbackStyleMedium
                            : UIImpactFeedbackStyleHeavy;
    UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
    [g prepare];
    [g impactOccurred];
}
