// G8.1a: Lundun share sheet. Opens Apple's share sheet (UIActivityViewController) with a share card image and one line
// of text, called from LundunShareCard.cs. TikTok, Instagram, WhatsApp, Messages and Save Image appear if available
// (Save Image needs NSPhotoLibraryAddUsageDescription, added to Info.plist by Editor/LundunIosBuild.cs).
// On iPad the sheet is a popover, anchored to the button that was tapped (x, y, w, h: 0..1 of the screen, from the top
// left).
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

extern "C" void LundunShareImage(const char* path, const char* text, float x, float y, float w, float h)
{
    if (path == NULL) { UnitySendMessage("LundunShareCard", "OnShareClosed", "failed"); return; }
    NSString* p = [NSString stringWithUTF8String:path];
    UIImage* image = [UIImage imageWithContentsOfFile:p];
    if (image == nil) { UnitySendMessage("LundunShareCard", "OnShareClosed", "failed"); return; }

    NSMutableArray* items = [NSMutableArray arrayWithObject:image];
    if (text != NULL)
    {
        NSString* t = [NSString stringWithUTF8String:text];
        if (t.length > 0) [items addObject:t];
    }

    UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];
    // tell the game when the sheet closes (shared or cancelled), so it can carry on (LundunShareCard.OnShareClosed)
    sheet.completionWithItemsHandler = ^(UIActivityType activityType, BOOL completed, NSArray* returnedItems, NSError* error) {
        UnitySendMessage("LundunShareCard", "OnShareClosed", completed ? "shared" : "cancelled");
    };
    UIViewController* root = UnityGetGLViewController();
    if (root == nil) { UnitySendMessage("LundunShareCard", "OnShareClosed", "failed"); return; }
    while (root.presentedViewController != nil) root = root.presentedViewController;

    UIPopoverPresentationController* pop = sheet.popoverPresentationController;
    if (pop != nil)                                   // iPad: anchor to the button
    {
        CGRect b = root.view.bounds;
        pop.sourceView = root.view;
        pop.sourceRect = CGRectMake(x * b.size.width, y * b.size.height, MAX(1.0, w * b.size.width), MAX(1.0, h * b.size.height));
        pop.permittedArrowDirections = UIPopoverArrowDirectionAny;
    }
    [root presentViewController:sheet animated:YES completion:nil];
}
