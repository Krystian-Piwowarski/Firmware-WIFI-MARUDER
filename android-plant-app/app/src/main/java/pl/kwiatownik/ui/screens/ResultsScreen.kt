package pl.kwiatownik.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.automirrored.filled.HelpOutline
import androidx.compose.material.icons.filled.LocalFlorist
import androidx.compose.material.icons.automirrored.filled.MenuBook
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import pl.kwiatownik.data.PlantMatch
import pl.kwiatownik.ui.IdentifyState
import pl.kwiatownik.ui.PlantViewModel
import pl.kwiatownik.ui.Screen

@Composable
fun ResultsScreen(vm: PlantViewModel) {
    Scaffold(topBar = { AppTopBar("Wynik rozpoznania", onBack = { vm.back() }) }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                AsyncImage(
                    model = vm.photoUri,
                    contentDescription = "Twoje zdjęcie",
                    contentScale = ContentScale.Crop,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(220.dp)
                        .clip(RoundedCornerShape(16.dp)),
                )
            }
            when (val state = vm.identifyState) {
                IdentifyState.Idle, IdentifyState.Loading -> item {
                    Column(
                        Modifier.fillMaxWidth().padding(32.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        CircularProgressIndicator()
                        Spacer(Modifier.height(16.dp))
                        Text("Rozpoznaję roślinę…")
                    }
                }

                is IdentifyState.Error -> item {
                    InfoCard(
                        icon = Icons.Filled.ErrorOutline,
                        title = "Coś poszło nie tak",
                        containerColor = MaterialTheme.colorScheme.errorContainer,
                        contentColor = MaterialTheme.colorScheme.onErrorContainer,
                    ) {
                        Text(state.message)
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = { vm.retry() }) { Text("Spróbuj ponownie") }
                            OutlinedButton(onClick = { vm.navigate(Screen.Settings) }) { Text("Ustawienia") }
                        }
                    }
                }

                is IdentifyState.Success -> {
                    val matches = state.matches
                    if (matches.isEmpty()) {
                        item {
                            InfoCard(icon = Icons.AutoMirrored.Filled.HelpOutline, title = "Nie rozpoznano rośliny") {
                                Text(
                                    "Spróbuj zrobić wyraźniejsze zdjęcie kwiatu lub liścia z bliska, " +
                                        "przy dobrym świetle."
                                )
                                OutlinedButton(onClick = { vm.back() }) { Text("Nowe zdjęcie") }
                            }
                        }
                    } else {
                        if (matches.first().score < 0.25) {
                            item {
                                InfoCard(
                                    icon = Icons.AutoMirrored.Filled.HelpOutline,
                                    title = "Niska pewność rozpoznania",
                                    containerColor = MaterialTheme.colorScheme.tertiaryContainer,
                                    contentColor = MaterialTheme.colorScheme.onTertiaryContainer,
                                ) {
                                    Text(
                                        "Porównaj zdjęcia poniżej ze swoją rośliną. Dla lepszego wyniku " +
                                            "zrób zdjęcie samego kwiatu z bliska."
                                    )
                                }
                            }
                        }
                        item {
                            Text(
                                "Najbardziej prawdopodobne gatunki – dotknij, aby zobaczyć opis i porady:",
                                style = MaterialTheme.typography.bodyMedium,
                            )
                        }
                        items(matches) { match ->
                            MatchCard(
                                match = match,
                                hasCare = vm.careRepository.find(match.scientificName, match.genus) != null,
                                onClick = { vm.openMatch(match) },
                            )
                        }
                        item {
                            OutlinedButton(onClick = { vm.back() }, modifier = Modifier.fillMaxWidth()) {
                                Text("Rozpoznaj inną roślinę")
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MatchCard(match: PlantMatch, hasCare: Boolean, onClick: () -> Unit) {
    Card(Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier
                    .size(76.dp)
                    .clip(RoundedCornerShape(12.dp)),
                contentAlignment = Alignment.Center,
            ) {
                if (match.imageUrl != null) {
                    AsyncImage(
                        model = match.imageUrl,
                        contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize(),
                    )
                } else {
                    Icon(Icons.Filled.LocalFlorist, contentDescription = null, modifier = Modifier.size(40.dp))
                }
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    match.commonNames.firstOrNull()?.replaceFirstChar { it.uppercase() } ?: match.scientificName,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                )
                Text(match.scientificName, style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic)
                if (match.family.isNotBlank()) {
                    Text("Rodzina: ${match.family}", style = MaterialTheme.typography.bodySmall)
                }
                Spacer(Modifier.height(4.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    LinearProgressIndicator(
                        progress = { match.score.toFloat().coerceIn(0f, 1f) },
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text("${(match.score * 100).toInt()}%", style = MaterialTheme.typography.labelMedium)
                }
                if (hasCare) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            Icons.AutoMirrored.Filled.MenuBook,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(16.dp),
                        )
                        Spacer(Modifier.width(4.dp))
                        Text(
                            "Szczegółowe porady w bazie",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                }
            }
        }
    }
}
