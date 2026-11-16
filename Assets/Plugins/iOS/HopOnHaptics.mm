// The light tap behind HopOn.UI.Haptics.Light: the same light impact iOS's own controls give.
#import <UIKit/UIKit.h>

extern "C" void HopOnHapticsLight(void)
{
    static UIImpactFeedbackGenerator *generator = nil;
    if (generator == nil)
    {
        generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    }
    [generator impactOccurred];
    [generator prepare];    // keeps the Taptic Engine ready, so the next tap is felt without delay
}
