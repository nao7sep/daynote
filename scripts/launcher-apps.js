// The platform adapter is the only owner of application discovery and ordinary quit requests.
// Path equality selects an app; it never authorizes a signal or force termination.
function matchingApplications(apps, paths, adapter) {
    return apps.filter(function (app) {
        return paths.indexOf(adapter.path(app)) !== -1 && !adapter.exited(app);
    });
}

function closeApplications(apps, paths, adapter, timeout) {
    var selected = matchingApplications(apps, paths, adapter);
    selected.forEach(function (app) {
        if (!adapter.exited(app) && !adapter.close(app))
            throw new Error('DayNote could not accept an ordinary quit request. Close it, then run the launcher again.');
    });
    var started = adapter.now();
    while (selected.some(function (app) { return !adapter.exited(app); })) {
        if (adapter.now() - started >= timeout)
            throw new Error('DayNote is still open. Finish saving or cancel this replacement, then run the launcher again.');
        adapter.pump();
    }
}

function run(args) {
    ObjC.import('AppKit');
    var applications = $.NSWorkspace.sharedWorkspace.runningApplications;
    var apps = [];
    for (var i = 0; i < applications.count; i++)
        apps.push(applications.objectAtIndex(i));
    var adapter = {
        path: function (app) { return app.executableURL ? ObjC.unwrap(app.executableURL.path) : ''; },
        exited: function (app) { return Boolean(app.isTerminated); },
        close: function (app) { return Boolean(app.terminate); },
        now: function () { return Number($.NSProcessInfo.processInfo.systemUptime); },
        // AppKit's changing properties update as the main run loop advances.
        pump: function () { $.NSRunLoop.currentRunLoop.runUntilDate($.NSDate.dateWithTimeIntervalSinceNow(0.05)); }
    };
    var mode = args[0];
    var paths = args.slice(1);
    if (mode === 'close') {
        closeApplications(apps, paths, adapter, 5);
        return 'closed';
    }
    if (mode === 'ready')
        return matchingApplications(apps, paths, adapter).length ? 'ready' : 'waiting';
    throw new Error('Unknown launcher application operation: ' + mode);
}
