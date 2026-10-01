package pl.kwiatownik

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import pl.kwiatownik.ui.PlantViewModel
import pl.kwiatownik.ui.Screen
import pl.kwiatownik.ui.screens.AtlasScreen
import pl.kwiatownik.ui.screens.DetailScreen
import pl.kwiatownik.ui.screens.HomeScreen
import pl.kwiatownik.ui.screens.ResultsScreen
import pl.kwiatownik.ui.screens.SettingsScreen
import pl.kwiatownik.ui.theme.KwiatownikTheme

class MainActivity : ComponentActivity() {
    private val vm: PlantViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            KwiatownikTheme {
                BackHandler(enabled = vm.backStack.size > 1) { vm.back() }
                when (vm.current) {
                    Screen.Home -> HomeScreen(vm)
                    Screen.Results -> ResultsScreen(vm)
                    Screen.Detail -> DetailScreen(vm)
                    Screen.Atlas -> AtlasScreen(vm)
                    Screen.Settings -> SettingsScreen(vm)
                }
            }
        }
    }
}
