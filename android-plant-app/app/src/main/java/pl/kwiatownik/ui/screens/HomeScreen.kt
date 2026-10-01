package pl.kwiatownik.ui.screens

import android.content.ActivityNotFoundException
import android.content.Context
import android.net.Uri
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.MenuBook
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Key
import androidx.compose.material.icons.filled.LocalFlorist
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import pl.kwiatownik.ui.PlantViewModel
import pl.kwiatownik.ui.Screen
import java.io.File

@Composable
fun HomeScreen(vm: PlantViewModel) {
    val context = LocalContext.current
    var pendingPhoto by rememberSaveable { mutableStateOf<Uri?>(null) }

    val takePicture = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val uri = pendingPhoto
        if (ok && uri != null) vm.identify(uri)
    }
    val pickImage = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) vm.identify(uri)
    }

    Scaffold(
        topBar = {
            AppTopBar("Kwiatownik", actions = {
                IconButton(onClick = { vm.navigate(Screen.Settings) }) {
                    Icon(Icons.Filled.Settings, contentDescription = "Ustawienia")
                }
            })
        }
    ) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.height(8.dp))
            Icon(
                Icons.Filled.LocalFlorist,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.secondary,
                modifier = Modifier.size(96.dp),
            )
            Text("Jaki to kwiat?", style = MaterialTheme.typography.headlineMedium)
            Text(
                "Zrób zdjęcie rośliny, a podpowiem, co to za gatunek, jak go uprawiać, " +
                    "na co uważać i czego nie robić.",
                style = MaterialTheme.typography.bodyLarge,
                textAlign = TextAlign.Center,
            )

            if (vm.settings.plantNetApiKey.isBlank()) {
                InfoCard(
                    icon = Icons.Filled.Key,
                    title = "Potrzebny klucz Pl@ntNet",
                    containerColor = MaterialTheme.colorScheme.errorContainer,
                    contentColor = MaterialTheme.colorScheme.onErrorContainer,
                ) {
                    Text("Rozpoznawanie korzysta z darmowego API Pl@ntNet. Wklej swój klucz w ustawieniach.")
                    OutlinedButton(onClick = { vm.navigate(Screen.Settings) }) { Text("Otwórz ustawienia") }
                }
            }

            Button(
                onClick = {
                    val uri = newPhotoUri(context)
                    pendingPhoto = uri
                    try {
                        takePicture.launch(uri)
                    } catch (e: ActivityNotFoundException) {
                        Toast.makeText(context, "Brak aplikacji aparatu", Toast.LENGTH_SHORT).show()
                    }
                },
                modifier = Modifier.fillMaxWidth().height(56.dp),
            ) {
                Icon(Icons.Filled.CameraAlt, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Zrób zdjęcie")
            }
            FilledTonalButton(
                onClick = {
                    pickImage.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                },
                modifier = Modifier.fillMaxWidth().height(56.dp),
            ) {
                Icon(Icons.Filled.PhotoLibrary, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Wybierz z galerii")
            }
            OutlinedButton(
                onClick = { vm.navigate(Screen.Atlas) },
                modifier = Modifier.fillMaxWidth().height(56.dp),
            ) {
                Icon(Icons.AutoMirrored.Filled.MenuBook, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Atlas kwiatów i porad (${vm.careRepository.all.size})")
            }

            InfoCard(icon = Icons.Filled.PhotoCamera, title = "Jak zrobić dobre zdjęcie") {
                BulletList(
                    listOf(
                        "Fotografuj jedną roślinę – najlepiej kwiat z bliska, wypełniający kadr.",
                        "Zadbaj o dobre, naturalne światło i ostrość.",
                        "Jeśli roślina nie kwitnie, sfotografuj liść z bliska.",
                        "Unikaj zdjęć bukietów, rysunków i roślin sztucznych.",
                    )
                )
            }
        }
    }
}

private fun newPhotoUri(context: Context): Uri {
    val dir = File(context.cacheDir, "images").apply { mkdirs() }
    val file = File(dir, "photo_${System.currentTimeMillis()}.jpg")
    return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
}
