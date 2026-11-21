// task 212: seconds since the last REAL keyboard / mouse / trackpad event (per event type, HID system state).
// ioreg's HIDIdleTime is reset every ~10 s by a running Unity player (user-activity declarations), so it cannot
// tell input apart while a player runs; these per-type counters only move on actual key / mouse / scroll events.
#include <ApplicationServices/ApplicationServices.h>
#include <stdio.h>
int main(void) {
    CGEventType t[] = { kCGEventKeyDown, kCGEventKeyUp, kCGEventFlagsChanged, kCGEventMouseMoved, kCGEventLeftMouseDown,
                        kCGEventRightMouseDown, kCGEventOtherMouseDown, kCGEventLeftMouseDragged, kCGEventScrollWheel };
    double m = 1e12;
    for (unsigned i = 0; i < sizeof t / sizeof t[0]; i++) {
        double s = CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateHIDSystemState, t[i]);
        if (s < m) m = s;
    }
    printf("%d\n", (int)m);
    return 0;
}
