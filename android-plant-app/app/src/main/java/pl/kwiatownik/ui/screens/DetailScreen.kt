package pl.kwiatownik.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.OpenInNew
import androidx.compose.material.icons.filled.Block
import androidx.compose.material.icons.filled.ContentCut
import androidx.compose.material.icons.filled.Grass
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lightbulb
import androidx.compose.material.icons.filled.LocalFlorist
import androidx.compose.material.icons.filled.Public
import androidx.compose.material.icons.filled.Science
import androidx.compose.material.icons.filled.Thermostat
import androidx.compose.material.icons.filled.WaterDrop
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material3.AssistChip
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import pl.kwiatownik.data.CareGuide
import pl.kwiatownik.ui.PlantViewModel
import pl.kwiatownik.ui.WikiState

@Composable
fun DetailScreen(vm: PlantViewModel) {
    val d = vm.detail ?: return
    val wiki = vm.wikiState
    val uriHandler = LocalUriHandler.current
    val headerImage = (wiki as? WikiState.Loaded)?.summary?.imageUrl ?: d.referenceImageUrl

    Scaffold(topBar = { AppTopBar(d.displayName, onBack = { vm.back() }) }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (headerImage != null) {
                item {
                    AsyncImage(
                        model = headerImage,
                        contentDescription = d.displayName,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(240.dp)
                            .clip(RoundedCornerShape(16.dp)),
                    )
                }
            }

            item {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(d.displayName, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                    Text(d.scientificName, style = MaterialTheme.typography.titleMedium, fontStyle = FontStyle.Italic)
                    if (d.family.isNotBlank()) Text("Rodzina: ${d.family}", style = MaterialTheme.typography.bodyMedium)
                    if (d.otherNames.isNotEmpty()) {
                        Text("Inne nazwy: ${d.otherNames.joinToString()}", style = MaterialTheme.typography.bodySmall)
                    }
                    if (d.score != null) {
                        Text(
                            "Pewność rozpoznania: ${(d.score * 100).toInt()}%",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                    d.care?.let { care ->
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (care.type.isNotBlank()) AssistChip(onClick = {}, label = { Text(care.type) })
                            if (care.difficulty.isNotBlank()) {
                                AssistChip(onClick = {}, label = { Text("Uprawa: ${care.difficulty}") })
                            }
                        }
                    }
                }
            }

            // Opis z Wikipedii
            item {
                InfoCard(icon = Icons.Filled.Public, title = "Opis") {
                    when (wiki) {
                        WikiState.Loading -> Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.width(12.dp))
                            Text("Szukam opisu w Wikipedii…")
                        }
                        WikiState.NotFound -> Text(
                            d.care?.description?.takeIf { it.isNotBlank() }
                                ?: "Nie znaleziono opisu tej rośliny w Wikipedii."
                        )
                        is WikiState.Loaded -> {
                            if (wiki.summary.isGenusOnly) {
                                Text(
                                    "Opis rodzaju ${wiki.summary.title}:",
                                    style = MaterialTheme.typography.labelLarge,
                                )
                            }
                            Text(wiki.summary.extract, style = MaterialTheme.typography.bodyMedium)
                            wiki.summary.pageUrl?.let { url ->
                                TextButton(onClick = { uriHandler.openUri(url) }) {
                                    Icon(Icons.AutoMirrored.Filled.OpenInNew, contentDescription = null)
                                    Spacer(Modifier.width(6.dp))
                                    Text("Czytaj więcej w Wikipedii")
                                }
                            }
                        }
                    }
                }
            }

            val care = d.care
            if (care != null) {
                careItems(care)
            } else {
                item { GenericCareCard() }
            }

            item {
                Text(
                    "Porady mają charakter ogólny – warunki w Twoim domu lub ogrodzie mogą wymagać korekt. " +
                        "W razie podejrzenia zatrucia skontaktuj się z lekarzem lub weterynarzem.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private fun androidx.compose.foundation.lazy.LazyListScope.careItems(care: CareGuide) {
    if (care.description.isNotBlank()) {
        item {
            InfoCard(icon = Icons.Filled.LocalFlorist, title = "O roślinie") {
                Text(care.description, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
    val sections: List<Triple<ImageVector, String, String>> = listOf(
        Triple(Icons.Filled.WbSunny, "Światło i stanowisko", care.light),
        Triple(Icons.Filled.WaterDrop, "Podlewanie", care.water),
        Triple(Icons.Filled.Grass, "Podłoże", care.soil),
        Triple(Icons.Filled.Thermostat, "Temperatura", care.temperature),
        Triple(Icons.Filled.Science, "Nawożenie", care.fertilizing),
        Triple(Icons.Filled.LocalFlorist, "Kwitnienie", care.flowering),
        Triple(Icons.Filled.ContentCut, "Przycinanie i pielęgnacja", care.pruning),
    ).filter { it.third.isNotBlank() }

    sections.forEach { (icon, title, text) ->
        item(key = title) {
            InfoCard(icon = icon, title = title) {
                Text(text, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }

    if (care.tips.isNotEmpty()) {
        item {
            InfoCard(
                icon = Icons.Filled.Lightbulb,
                title = "Na co zwrócić uwagę",
                containerColor = MaterialTheme.colorScheme.primaryContainer,
                contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
            ) { BulletList(care.tips) }
        }
    }
    if (care.avoid.isNotEmpty()) {
        item {
            InfoCard(
                icon = Icons.Filled.Block,
                title = "Czego nie robić",
                containerColor = MaterialTheme.colorScheme.errorContainer,
                contentColor = MaterialTheme.colorScheme.onErrorContainer,
            ) { BulletList(care.avoid) }
        }
    }
    if (care.toxicity.isNotBlank()) {
        item {
            InfoCard(
                icon = Icons.Filled.Warning,
                title = "Toksyczność (dzieci i zwierzęta)",
                containerColor = MaterialTheme.colorScheme.tertiaryContainer,
                contentColor = MaterialTheme.colorScheme.onTertiaryContainer,
            ) { Text(care.toxicity, style = MaterialTheme.typography.bodyMedium) }
        }
    }
}

@Composable
private fun GenericCareCard() {
    InfoCard(icon = Icons.Filled.Info, title = "Ogólne zasady uprawy") {
        Text(
            "Tego gatunku nie ma jeszcze w bazie szczegółowych porad. Oto zasady, które sprawdzą się " +
                "w przypadku większości roślin kwitnących:",
            style = MaterialTheme.typography.bodyMedium,
        )
        BulletList(
            listOf(
                "Podlewaj dopiero, gdy wierzchnia warstwa ziemi (2–3 cm) przeschnie – częściej szkodzi nadmiar niż brak wody.",
                "Używaj doniczek z otworami odpływowymi i warstwy drenażu; nie zostawiaj wody w podstawce.",
                "Większość roślin kwitnących potrzebuje dużo jasnego światła – kwiaty są pierwszym, co roślina traci przy jego braku.",
                "W sezonie wzrostu (wiosna–lato) nawoź co 2–3 tygodnie nawozem do roślin kwitnących, zimą przerwij.",
                "Usuwaj przekwitłe kwiaty – roślina zakwitnie ponownie zamiast zawiązywać nasiona.",
                "Regularnie oglądaj spód liści – wcześnie wykryte szkodniki (mszyce, przędziorki) łatwo zwalczyć.",
                "Zanim postawisz roślinę w zasięgu dzieci lub zwierząt, sprawdź, czy nie jest trująca.",
            )
        )
    }
}
