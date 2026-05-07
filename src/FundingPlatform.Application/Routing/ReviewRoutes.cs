namespace FundingPlatform.Application.Routing;

// Single source of truth for the reviewer "review one application" route.
// ReviewController declares the route via [Route(ReviewTemplate)]; projections
// emit hrefs via PathFor(id). Keeps producer (href emitter) and consumer
// (route handler) from drifting — the spec-013 404 came from a hand-built
// "/Review/Review/{id}" string in the projection that never matched the
// controller's "Review/{id:int}" template.
public static class ReviewRoutes
{
    public const string ReviewTemplate = "Review/{id:int}";

    public static string PathFor(int applicationId) => $"/Review/{applicationId}";

    public static string DeepLinkFor(int applicationId, int versionEventId) =>
        $"/Review/{applicationId}#event-{versionEventId}";
}
