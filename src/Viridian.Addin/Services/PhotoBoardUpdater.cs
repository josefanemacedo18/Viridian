using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Viridian.Library;
using Viridian.Revit;

namespace Viridian.Addin.Services;

/// <summary>
/// Mantém os cartões do quadro de fotos ligados às plantas: quando o usuário arrasta a foto (ou move o exemplar),
/// o texto acompanha a foto e a linha de ligação é refeita na mesma operação (um único passo de desfazer).
/// </summary>
internal sealed class PhotoBoardUpdater : IUpdater
{
    private static readonly Guid UpdaterGuid = new("6f3f1a63-2c8a-4e57-9a31-0b5d1d7c2a44");
    private readonly UpdaterId _id;

    public PhotoBoardUpdater(AddInId addInId)
    {
        _id = new UpdaterId(addInId, UpdaterGuid);
    }

    public static void Register(AddInId addInId)
    {
        try
        {
            var updater = new PhotoBoardUpdater(addInId);
            if (UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId()))
            {
                return;
            }

            // Opcional: projetos abertos sem o Viridian não mostram aviso de atualizador ausente.
            UpdaterRegistry.RegisterUpdater(updater, true);
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(ImageInstance)), Element.GetChangeTypeGeometry());
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementCategoryFilter(BuiltInCategory.OST_Planting), Element.GetChangeTypeGeometry());
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Quadro de fotos: o acompanhamento das fotos movidas não pôde ser ativado.", ex);
        }
    }

    public static void Unregister(AddInId addInId)
    {
        try
        {
            var id = new UpdaterId(addInId, UpdaterGuid);
            if (UpdaterRegistry.IsUpdaterRegistered(id))
            {
                UpdaterRegistry.UnregisterUpdater(id);
            }
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Quadro de fotos: falha ao desligar o acompanhamento.", ex);
        }
    }

    public void Execute(UpdaterData data)
    {
        try
        {
            Document document = data.GetDocument();
            var cards = new HashSet<string>();
            var plants = new List<string>();
            foreach (ElementId id in data.GetModifiedElementIds())
            {
                Element? element = document.GetElement(id);
                if (element is ImageInstance image)
                {
                    if (ElementData.TryRead(image, ElementData.KindPhotoBoard, out PhotoBoardLink link) && link.CardId.Length > 0)
                    {
                        cards.Add(link.CardId);
                    }
                }
                else if (element is FamilyInstance plant)
                {
                    plants.Add(plant.UniqueId);
                }
            }

            if (plants.Count > 0)
            {
                foreach (string card in PhotoService.CardsOfTarget(document, plants.ToHashSet(StringComparer.Ordinal)))
                {
                    cards.Add(card);
                }
            }

            foreach (string card in cards)
            {
                PhotoService.FollowCard(document, card);
            }
        }
        catch (Exception ex)
        {
            // Nunca interrompe a edição do usuário: no pior caso a linha fica onde estava.
            ViridianLog.Warn("Quadro de fotos: não foi possível acompanhar a foto movida.", ex);
        }
    }

    public UpdaterId GetUpdaterId() => _id;

    public ChangePriority GetChangePriority() => ChangePriority.Annotations;

    public string GetUpdaterName() => "Viridian: quadro de fotos";

    public string GetAdditionalInformation() => "Mantém a linha de ligação e o texto dos cartões do quadro de fotos acompanhando a foto e a planta.";
}
