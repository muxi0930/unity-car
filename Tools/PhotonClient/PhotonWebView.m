#import <AppKit/AppKit.h>
#import <WebKit/WebKit.h>
#import <QuartzCore/QuartzCore.h>

static WKWebView *photonView;
static NSMutableArray<NSString *> *photonMessages;
static BOOL sizedPlayerWindow;
static NSRect placedEditorRect, placedWindowFrame;
static NSMenuItem *photonEditMenu;
static id photonKeyMonitor;

static void runJS(NSString *script);

@interface PhotonWebReceiver : NSObject <WKScriptMessageHandler, WKNavigationDelegate>
- (void)pasteText:(id)sender;
- (void)selectText:(id)sender;
@end

static void enqueue(NSDictionary *body) {
    if (!photonMessages || ![NSJSONSerialization isValidJSONObject:body]) return;
    NSData *json = [NSJSONSerialization dataWithJSONObject:body options:0 error:NULL];
    NSString *text = [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding];
    if (!text) return;
    // Frame snapshots can coalesce only when the discarded snapshot adds no marks.
    NSUInteger tail=photonMessages.count;
    if ([body[@"type"] isEqual:@"frame"] && tail>0) {
        NSString *last=photonMessages[tail-1];
        if ([last containsString:@"\"type\":\"frame\""] &&
            [last containsString:@"\"marks\":[]"] && [last containsString:@"\"clearMarks\":false"]) {
            photonMessages[tail-1]=text;
            return;
        }
    }
    [photonMessages addObject:text];
}

@implementation PhotonWebReceiver
- (void)pasteText:(id)sender {
    NSString *text=[NSPasteboard.generalPasteboard stringForType:NSPasteboardTypeString];
    if (!text) return;
    NSData *data=[NSJSONSerialization dataWithJSONObject:@[text] options:0 error:NULL];
    NSString *json=[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    runJS([NSString stringWithFormat:@"(()=>{const e=document.activeElement;if(e?.selectionStart==null || e.readOnly || e.disabled)return; const text=%@[0];e.setRangeText(text,e.selectionStart,e.selectionEnd,'end');e.dispatchEvent(new Event('input',{bubbles:true}));})()",json]);
}
- (void)selectText:(id)sender {
    runJS(@"document.activeElement?.select?.()");
}
- (void)userContentController:(WKUserContentController *)controller didReceiveScriptMessage:(WKScriptMessage *)message {
    if (message.frameInfo.mainFrame && [message.body isKindOfClass:NSDictionary.class]) enqueue(message.body);
}
- (void)webView:(WKWebView *)view decidePolicyForNavigationAction:(WKNavigationAction *)action
decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    decisionHandler(action.request.URL.isFileURL ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}
- (void)webView:(WKWebView *)view didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    enqueue(@{@"type":@"error", @"message":error.localizedDescription});
}
- (void)webViewWebContentProcessDidTerminate:(WKWebView *)view {
    enqueue(@{@"type":@"error", @"message":@"WebKit 游戏界面进程已退出"});
}
@end

static PhotonWebReceiver *photonReceiver;
static NSObject *photonFullscreenObserver, *photonWindowedObserver;

static void runJS(NSString *script) {
    [photonView evaluateJavaScript:script completionHandler:nil];
}

static NSWindow *gameWindow(BOOL editor, NSRect frame) {
    if (!editor) {
        // Unity's player window can exist before AppKit makes it key/main,
        // especially when an app is started in the background.
        NSWindow *largest=nil;
        CGFloat size=0;
        for (NSWindow *window in NSApp.windows) {
            if (!window.isVisible || !window.contentView || window.isMiniaturized) continue;
            NSSize content=window.contentView.bounds.size;
            CGFloat area=content.width*content.height;
            if (area>size) {size=area;largest=window;}
        }
        return largest;
    }
    NSPoint center=NSMakePoint(NSMidX(frame),NSMidY(frame));
    for (NSWindow *window in NSApp.orderedWindows)
        if (window.isVisible && window.contentView && NSPointInRect(center,window.frame)) return window;
    return nil;
}

static void installEditMenu(void) {
    NSMenu *main=NSApp.mainMenu;
    if (!main || photonEditMenu.menu==main) return;
    SEL paste=NSSelectorFromString(@"paste:");
    for (NSMenuItem *item in main.itemArray)
        if ([item.submenu indexOfItemWithTarget:nil andAction:paste]>=0 && item.submenu) return;
    NSMenu *edit=[[NSMenu alloc] initWithTitle:@"编辑"];
    [edit addItemWithTitle:@"剪切" action:NSSelectorFromString(@"cut:") keyEquivalent:@"x"];
    [edit addItemWithTitle:@"复制" action:NSSelectorFromString(@"copy:") keyEquivalent:@"c"];
    [edit addItemWithTitle:@"粘贴" action:@selector(pasteText:) keyEquivalent:@"v"].target=photonReceiver;
    [edit addItem:NSMenuItem.separatorItem];
    [edit addItemWithTitle:@"全选" action:@selector(selectText:) keyEquivalent:@"a"].target=photonReceiver;
    photonEditMenu=[[NSMenuItem alloc] initWithTitle:@"编辑" action:NULL keyEquivalent:@""];
    photonEditMenu.submenu=edit;
    [main insertItem:photonEditMenu atIndex:MIN(1,main.numberOfItems)];
}

static void place(BOOL editor, double x, double y, double width, double height, BOOL visible) {
    if (!photonView) return;
    if (!visible) {
        if (!photonView.hidden) photonView.hidden=YES;
        return;
    }

    NSRect requested=NSMakeRect(x,y,width,height);
    NSWindow *attached=photonView.window;
    NSView *attachedContent=attached.contentView;
    // The Player view autoresizes with its host. The Editor view is anchored to the Game tab.
    if (!photonView.hidden && attached.isVisible && !attached.isMiniaturized && photonView.superview==attachedContent) {
        if (editor && NSEqualRects(requested,placedEditorRect) && NSEqualRects(attached.frame,placedWindowFrame)) return;
        if (!editor && NSEqualRects(photonView.frame,attachedContent.bounds)) return;
    }

    NSRect absolute=NSZeroRect;
    if (editor) {
        NSScreen *first=NSScreen.screens.firstObject;
        absolute=NSMakeRect(x,NSMaxY(first.frame)-y-height,width,height);
    }
    NSWindow *window=gameWindow(editor,absolute);
    photonView.hidden=!window;
    if (!window) return;
    NSView *container=window.contentView;
    if (!editor && !sizedPlayerWindow) {
        sizedPlayerWindow=YES;
        installEditMenu();
        photonKeyMonitor=[NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskKeyDown handler:^NSEvent *(NSEvent *event) {
            if (event.window!=photonView.window || !(event.modifierFlags & NSEventModifierFlagCommand)) return event;
            NSResponder *responder=event.window.firstResponder;
            if (![responder isKindOfClass:NSView.class] || ![(NSView *)responder isDescendantOf:photonView]) return event;
            NSString *key=event.charactersIgnoringModifiers.lowercaseString;
            if ([key isEqualToString:@"v"]) { [photonReceiver pasteText:nil]; return nil; }
            if ([key isEqualToString:@"a"]) { [photonReceiver selectText:nil]; return nil; }
            return event;
        }];
        // Unity's default is measured in backing pixels; on Retina that can
        // otherwise open a 640 point window and activate the touch layout.
        NSRect available=(window.screen ?: NSScreen.mainScreen).visibleFrame;
        CGFloat width=MIN(1120,MIN(NSWidth(available)-32,(NSHeight(available)-64)*16.0/9.0));
        if (width>NSWidth(container.bounds) && !(window.styleMask & NSWindowStyleMaskFullScreen)) {
            [window setContentSize:NSMakeSize(width,width*9.0/16.0)];
            [window center];
        }
    }
    if (photonView.superview!=container) {
        [photonView removeFromSuperview];
        photonView.wantsLayer=YES;
        [container addSubview:photonView positioned:NSWindowAbove relativeTo:nil];
        photonView.layer.zPosition=100;
        [window makeFirstResponder:photonView];
    }
    if (editor) {
        photonView.autoresizingMask=NSViewNotSizable;
        photonView.frame=[container convertRect:[window convertRectFromScreen:absolute] fromView:nil];
        placedEditorRect=requested;
        placedWindowFrame=window.frame;
    } else {
        photonView.frame=container.bounds;
        photonView.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;
    }
}

__attribute__((visibility("default"))) void PhotonOpen(const char *path, int editor) {
    if (photonView) return;
    photonMessages=[NSMutableArray array];
    photonReceiver=[PhotonWebReceiver new];
    WKWebViewConfiguration *configuration=[WKWebViewConfiguration new];
    [configuration.userContentController addScriptMessageHandler:photonReceiver name:@"photon"];
    photonView=[[WKWebView alloc] initWithFrame:NSZeroRect configuration:configuration];
    photonView.navigationDelegate=photonReceiver;
    [photonView setValue:@NO forKey:@"drawsBackground"];
    if (@available(macOS 13.3, *)) photonView.inspectable=YES;
    NSString *file=[NSString stringWithUTF8String:path];
    NSURL *url=[NSURL fileURLWithPath:file];
    [photonView loadFileURL:url allowingReadAccessToURL:url.URLByDeletingLastPathComponent];
    if (!editor) place(NO,0,0,0,0,YES);
    NSNotificationCenter *center=NSNotificationCenter.defaultCenter;
    photonFullscreenObserver=[center addObserverForName:NSWindowDidEnterFullScreenNotification object:nil queue:nil usingBlock:^(NSNotification *n){
        if (n.object==photonView.window) runJS(@"window.photonFullscreenChanged?.(true)");
    }];
    photonWindowedObserver=[center addObserverForName:NSWindowDidExitFullScreenNotification object:nil queue:nil usingBlock:^(NSNotification *n){
        if (n.object==photonView.window) runJS(@"window.photonFullscreenChanged?.(false)");
    }];
}

__attribute__((visibility("default"))) void PhotonPlace(int editor, double x, double y, double width, double height, int visible) {
    place(editor!=0,x,y,width,height,visible!=0);
}
__attribute__((visibility("default"))) void PhotonEvaluate(const char *script) {
    if (photonView && script) runJS([NSString stringWithUTF8String:script]);
}
__attribute__((visibility("default"))) int PhotonPoll(unsigned char *buffer, int capacity) {
    if (photonMessages.count==0) return 0;
    NSData *data=[photonMessages.firstObject dataUsingEncoding:NSUTF8StringEncoding];
    if (data.length>capacity) return -(int)data.length;
    [data getBytes:buffer length:data.length];
    [photonMessages removeObjectAtIndex:0];
    return (int)data.length;
}
__attribute__((visibility("default"))) void PhotonFullscreen(void) {
    [photonView.window toggleFullScreen:nil];
}
__attribute__((visibility("default"))) void PhotonClose(void) {
    if (!photonView) return;
    [NSNotificationCenter.defaultCenter removeObserver:photonFullscreenObserver];
    [NSNotificationCenter.defaultCenter removeObserver:photonWindowedObserver];
    [photonView.configuration.userContentController removeScriptMessageHandlerForName:@"photon"];
    [photonView stopLoading];
    [photonView removeFromSuperview];
    photonView=nil;
    photonReceiver=nil;
    photonMessages=nil;
    sizedPlayerWindow=NO;
    [photonEditMenu.menu removeItem:photonEditMenu];
    photonEditMenu=nil;
    if (photonKeyMonitor) [NSEvent removeMonitor:photonKeyMonitor];
    photonKeyMonitor=nil;
    photonFullscreenObserver=nil;
    photonWindowedObserver=nil;
}
