package pl.kwiatownik.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import pl.kwiatownik.ui.PlantViewModel

@Composable
fun AtlasScreen(vm: PlantViewModel) {
    var query by rememberSaveable { mutableStateOf("") }
    val q = query.trim().lowercase()
    val plants = vm.careRepository.all.filter {
        q.isEmpty() || it.namePl.lowercase().contains(q) || it.latin.lowercase().contains(q) ||
            it.type.lowercase().contains(q)
    }

    Scaffold(topBar = { AppTopBar("Atlas kwiatów", onBack = { vm.back() }) }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            item {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                    placeholder = { Text("Szukaj, np. róża, storczyk, balkon…") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            items(plants, key = { it.id }) { care ->
                Card(Modifier.fillMaxWidth().clickable { vm.openCareGuide(care) }) {
                    Column(Modifier.padding(16.dp)) {
                        Text(care.namePl, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        Text(care.latin, style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic)
                        Text(
                            "${care.type} · uprawa: ${care.difficulty}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
            if (plants.isEmpty()) {
                item { Text("Brak wyników dla „$query”.") }
            }
        }
    }
}
