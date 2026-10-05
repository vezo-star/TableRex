using BookingPlatform.Api.Security;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Venues;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/venues")]
[Produces("application/json")]
public sealed class VenuesController(VenueService venues) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.VenueView)]
    public Task<IReadOnlyList<VenueDto>> List(CancellationToken cancellationToken) => venues.ListAsync(cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.VenueManage)]
    [ProducesResponseType<VenueDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<VenueDto>> Create(CreateVenueRequest request, CancellationToken cancellationToken)
    {
        var venue = await venues.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { venueId = venue.Id }, venue);
    }

    [HttpGet("{venueId:guid}")]
    [HasPermission(Permissions.VenueView)]
    public Task<VenueDto> Get(Guid venueId, CancellationToken cancellationToken) => venues.GetAsync(venueId, cancellationToken);

    [HttpPut("{venueId:guid}")]
    [HasPermission(Permissions.VenueManage)]
    public Task<VenueDto> Update(Guid venueId, UpdateVenueRequest request, CancellationToken cancellationToken) =>
        venues.UpdateAsync(venueId, request, cancellationToken);

    [HttpGet("{venueId:guid}/sections")]
    [HasPermission(Permissions.VenueView)]
    public Task<IReadOnlyList<SectionDto>> ListSections(Guid venueId, CancellationToken cancellationToken) =>
        venues.ListSectionsAsync(venueId, cancellationToken);

    [HttpPost("{venueId:guid}/sections")]
    [HasPermission(Permissions.VenueManage)]
    [ProducesResponseType<SectionDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SectionDto>> CreateSection(Guid venueId, SectionRequest request, CancellationToken cancellationToken)
    {
        var section = await venues.CreateSectionAsync(venueId, request, cancellationToken);
        return Created($"/api/v1/venues/{venueId}/sections/{section.Id}", section);
    }

    [HttpPut("{venueId:guid}/sections/{sectionId:guid}")]
    [HasPermission(Permissions.VenueManage)]
    public Task<SectionDto> UpdateSection(Guid venueId, Guid sectionId, SectionRequest request, CancellationToken cancellationToken) =>
        venues.UpdateSectionAsync(venueId, sectionId, request, cancellationToken);

    /// <summary>Deletes the section, or deactivates it when resources still reference it.</summary>
    [HttpDelete("{venueId:guid}/sections/{sectionId:guid}")]
    [HasPermission(Permissions.VenueManage)]
    public Task<DeleteResult> DeleteSection(Guid venueId, Guid sectionId, CancellationToken cancellationToken) =>
        venues.DeleteSectionAsync(venueId, sectionId, cancellationToken);

    [HttpGet("{venueId:guid}/operating-hours")]
    [HasPermission(Permissions.VenueView)]
    public Task<IReadOnlyList<OperatingHoursPeriod>> GetOperatingHours(Guid venueId, CancellationToken cancellationToken) =>
        venues.GetOperatingHoursAsync(venueId, cancellationToken);

    /// <summary>Replaces the weekly schedule. Day of week is ISO (1 = Monday … 7 = Sunday); close ≤ open means overnight.</summary>
    [HttpPut("{venueId:guid}/operating-hours")]
    [HasPermission(Permissions.VenueManage)]
    public Task<IReadOnlyList<OperatingHoursPeriod>> ReplaceOperatingHours(
        Guid venueId, ReplaceOperatingHoursRequest request, CancellationToken cancellationToken) =>
        venues.ReplaceOperatingHoursAsync(venueId, request, cancellationToken);

    [HttpGet("{venueId:guid}/special-hours")]
    [HasPermission(Permissions.VenueView)]
    public Task<IReadOnlyList<SpecialHoursDto>> ListSpecialHours(
        Guid venueId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        venues.ListSpecialHoursAsync(venueId, from, to, cancellationToken);

    [HttpPost("{venueId:guid}/special-hours")]
    [HasPermission(Permissions.VenueManage)]
    [ProducesResponseType<SpecialHoursDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SpecialHoursDto>> CreateSpecialHours(Guid venueId, SpecialHoursRequest request, CancellationToken cancellationToken)
    {
        var entry = await venues.CreateSpecialHoursAsync(venueId, request, cancellationToken);
        return Created($"/api/v1/venues/{venueId}/special-hours/{entry.Id}", entry);
    }

    [HttpPut("{venueId:guid}/special-hours/{id:guid}")]
    [HasPermission(Permissions.VenueManage)]
    public Task<SpecialHoursDto> UpdateSpecialHours(Guid venueId, Guid id, SpecialHoursRequest request, CancellationToken cancellationToken) =>
        venues.UpdateSpecialHoursAsync(venueId, id, request, cancellationToken);

    [HttpDelete("{venueId:guid}/special-hours/{id:guid}")]
    [HasPermission(Permissions.VenueManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSpecialHours(Guid venueId, Guid id, CancellationToken cancellationToken)
    {
        await venues.DeleteSpecialHoursAsync(venueId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Effective opening periods per venue-local business date (max 62 days).</summary>
    [HttpGet("{venueId:guid}/schedule")]
    [HasPermission(Permissions.VenueView)]
    public Task<VenueScheduleDto> GetSchedule(Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        venues.GetScheduleAsync(venueId, from, to, cancellationToken);
}

[ApiController]
[Route("api/v1/resource-types")]
[Produces("application/json")]
public sealed class ResourceTypesController(ResourceService resources) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.ResourceView)]
    public Task<IReadOnlyList<ResourceTypeDto>> List(CancellationToken cancellationToken) => resources.ListTypesAsync(cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.ResourceCreate)]
    [ProducesResponseType<ResourceTypeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ResourceTypeDto>> Create(CreateResourceTypeRequest request, CancellationToken cancellationToken)
    {
        var type = await resources.CreateTypeAsync(request, cancellationToken);
        return Created($"/api/v1/resource-types/{type.Id}", type);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ResourceUpdate)]
    public Task<ResourceTypeDto> Update(Guid id, UpdateResourceTypeRequest request, CancellationToken cancellationToken) =>
        resources.UpdateTypeAsync(id, request, cancellationToken);
}

[ApiController]
[Route("api/v1/resources")]
[Produces("application/json")]
public sealed class ResourcesController(ResourceService resources) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.ResourceView)]
    public Task<PagedResult<ResourceDto>> List(
        [FromQuery] Guid? venueId, [FromQuery] Guid? sectionId, [FromQuery] Guid? resourceTypeId, [FromQuery] bool? isActive,
        [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        resources.ListAsync(new ResourceQuery(venueId, sectionId, resourceTypeId, isActive, new PageRequest(page, pageSize)), cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.ResourceCreate)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ResourceDto>> Create(CreateResourceRequest request, CancellationToken cancellationToken)
    {
        var resource = await resources.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = resource.Id }, resource);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.ResourceView)]
    public Task<ResourceDto> Get(Guid id, CancellationToken cancellationToken) => resources.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ResourceUpdate)]
    public Task<ResourceDto> Update(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken) =>
        resources.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deletes the resource, or deactivates it when it has booking history.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.ResourceDelete)]
    public Task<DeleteResult> Delete(Guid id, CancellationToken cancellationToken) => resources.DeleteAsync(id, cancellationToken);
}

[ApiController]
[Route("api/v1/floor-plans")]
[Produces("application/json")]
public sealed class FloorPlansController(FloorPlanService floorPlans) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.FloorPlanView)]
    public Task<IReadOnlyList<FloorPlanDto>> List([FromQuery] Guid venueId, CancellationToken cancellationToken) =>
        floorPlans.ListAsync(venueId, cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.FloorPlanManage)]
    [ProducesResponseType<FloorPlanDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FloorPlanDto>> Create(CreateFloorPlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await floorPlans.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.FloorPlanView)]
    public Task<FloorPlanDto> Get(Guid id, CancellationToken cancellationToken) => floorPlans.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.FloorPlanManage)]
    public Task<FloorPlanDto> Update(Guid id, UpdateFloorPlanRequest request, CancellationToken cancellationToken) =>
        floorPlans.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.FloorPlanManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await floorPlans.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Replaces all resource positions on the plan (bulk save from the editor).</summary>
    [HttpPut("{id:guid}/resources")]
    [HasPermission(Permissions.FloorPlanManage)]
    public Task<FloorPlanDto> ReplacePositions(Guid id, ReplaceFloorPlanPositionsRequest request, CancellationToken cancellationToken) =>
        floorPlans.ReplacePositionsAsync(id, request, cancellationToken);
}
