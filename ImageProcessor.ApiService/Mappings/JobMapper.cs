using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.Data.Models.Domain;
using Riok.Mapperly.Abstractions;
using System.Text.Json;

namespace ImageProcessor.ApiService.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)] // remove warnings for unmapped source members
public partial class JobMapper
{
    public partial JobResponse ToResponse(Job job);

    private static string MapStatus(JobStatus status) => status.ToString();

    private static Dictionary<string, string>? MapJsonDocument(JsonDocument? document)
    {
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString()
            );
    }

    private static JobMetadataResponse? MapToJobMetadataResponse(JsonDocument? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata.RootElement.Deserialize<JobMetadataResponse>();
    }
}
