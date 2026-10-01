package pl.kwiatownik.ui.screens

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Key
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp
import pl.kwiatownik.ui.PlantViewModel

@Composable
fun SettingsScreen(vm: PlantViewModel) {
    val context = LocalContext.current
    val uriHandler = LocalUriHandler.current
    var key by rememberSaveable { mutableStateOf(vm.settings.plantNetApiKey) }

    Scaffold(topBar = { AppTopBar("Ustawienia", onBack = { vm.back() }) }) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            InfoCard(icon = Icons.Filled.Key, title = "Klucz API Pl@ntNet") {
                Text(
                    "Rozpoznawanie roślin działa dzięki serwisowi Pl@ntNet. Klucz jest darmowy " +
                        "(do 500 rozpoznań dziennie):"
                )
                BulletList(
                    listOf(
                        "Załóż konto na my.plantnet.org.",
                        "W zakładce „Settings” / „API key” skopiuj swój klucz.",
                        "Wklej go poniżej i zapisz.",
                    )
                )
                OutlinedButton(onClick = { uriHandler.openUri("https://my.plantnet.org/") }) {
                    Text("Otwórz my.plantnet.org")
                }
            }
            OutlinedTextField(
                value = key,
                onValueChange = { key = it },
                label = { Text("Klucz API") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = {
                    vm.settings.plantNetApiKey = key
                    Toast.makeText(context, "Zapisano", Toast.LENGTH_SHORT).show()
                    vm.back()
                },
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Zapisz") }

            InfoCard(icon = Icons.Filled.Info, title = "O aplikacji") {
                Text(
                    "Kwiatownik rozpoznaje rośliny przez Pl@ntNet, opisy pobiera z Wikipedii, a porady " +
                        "uprawowe pochodzą z wbudowanej bazy (${vm.careRepository.all.size} popularnych " +
                        "kwiatów i roślin doniczkowych). Zdjęcia są wysyłane wyłącznie do Pl@ntNet " +
                        "w celu rozpoznania."
                )
            }
        }
    }
}
