using Assistant.Core;
using Assistant.Extensions.Notes.Contributors;
using Assistant.Extensions.Notes.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Notes;

public static class NotesServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantNotes(this IServiceCollection services)
    {
        services.AddAssistantTool<CreateNoteTool>();
        services.AddAssistantTool<SearchNotesTool>();
        services.AddAssistantTool<ReadNoteTool>();
        services.AddAssistantTool<UpdateNoteTool>();
        services.AddAssistantTool<LinkNotesTool>();
        services.AddContextContributor<RelatedNotesContributor>();
        return services;
    }
}
