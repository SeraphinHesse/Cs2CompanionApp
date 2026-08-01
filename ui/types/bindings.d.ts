declare module "cs2/bindings" {
  import { ChartDataset } from 'chart.js';
  
  export interface ServiceBudget {
  	id: string;
  	value: number;
  }
  export interface ServiceFee {
  	incomeInternal: number;
  	incomeExports: number;
  	expenseImport: number;
  }
  export interface ServiceBuildingBudgetInfo {
  	id: string;
  	serviceBuildings: string[];
  }
  export interface ServiceBuildingBudgetData {
  	id: string;
  	count: number;
  	employees: number;
  	maxEmployees: number;
  }
  export interface Entity {
  	index: number;
  	version: number;
  }
  export interface ValueBinding<T> {
  	readonly value: T;
  	subscribe(listener?: BindingListener<T>): ValueSubscription<T>;
  	dispose(): void;
  }
  export interface MapBinding<K, V> {
  	getValue(key: K): V;
  	subscribe(key: K, listener?: BindingListener<V>): ValueSubscription<V>;
  	dispose(): void;
  }
  export interface EventBinding<T> {
  	subscribe(listener: BindingListener<T>): Subscription;
  	dispose(): void;
  }
  export interface BindingListener<T> {
  	(value: T): void;
  }
  export interface Subscription {
  	dispose(): void;
  }
  export interface ValueSubscription<T> extends Subscription {
  	readonly value: T;
  	setChangeListener(listener: BindingListener<T>): void;
  }
  const focusedEntity$: ValueBinding<Entity>;
  function focusEntity(entity: Entity): void;
  export interface Typed<T extends string> {
  	__Type: T;
  }
  export type TypeFromMap<T extends Record<string, any>> = {
  	[K in keyof T]: K extends string ? T[K] & Typed<K> : never;
  }[keyof T];
  export enum NameType {
  	Custom = "names.CustomName",
  	Localized = "names.LocalizedName",
  	Formatted = "names.FormattedName"
  }
  export type Name = CustomName | LocalizedName | FormattedName;
  export interface CustomName extends Typed<NameType.Custom> {
  	name: string;
  }
  export interface LocalizedName extends Typed<NameType.Localized> {
  	nameId: string;
  }
  export interface FormattedName extends Typed<NameType.Formatted> {
  	nameId: string;
  	nameArgs: {
  		[key: string]: string;
  	};
  }
  export interface Chirp {
  	entity: Entity;
  	sender: ChirpSender;
  	date: number;
  	messageId: string;
  	links: ChirpLink[];
  	likes: number;
  	liked: boolean;
  }
  export interface ChirpSender {
  	entity: Entity;
  	link: ChirpLink;
  }
  export interface ChirpLink {
  	name: Name;
  	target: string;
  }
  const chirps$: ValueBinding<Chirp[]>;
  const chirpAdded$: EventBinding<Chirp>;
  function addLike(entity: Entity): void;
  function removeLike(entity: Entity): void;
  function selectLink(target: string): void;
  export interface Number2 {
  	readonly x: number;
  	readonly y: number;
  }
  export interface Bounds1 {
  	readonly min: number;
  	readonly max: number;
  }
  export type LongNumber = [
  	number,
  	number
  ];
  export enum WeightedMode {
  	None = 0,
  	In = 1,
  	Out = 2,
  	Both = 3
  }
  export enum WrapMode {
  	Default = 0,
  	Clamp = 1,
  	Once = 1,
  	Loop = 2,
  	PingPong = 4,
  	ClampForever = 8
  }
  export interface IKeyframeMetadata {
  	deviationBase?: number;
  }
  export interface IKeyframe extends IKeyframeMetadata {
  	time: number;
  	value: number;
  	inTangent: number;
  	outTangent: number;
  	inWeight: number;
  	outWeight: number;
  	weightedMode: WeightedMode;
  	readonly?: boolean;
  }
  export interface AnimationCurveMetadata<T = string> {
  	id?: string;
  	color?: string;
  	handleColor?: string;
  	hidePath?: boolean;
  	deviationFrom?: T;
  	min?: T;
  	max?: T;
  	hidden?: boolean;
  	readonly?: boolean;
  	legend?: string;
  	hideTooltip?: boolean;
  	showCursor?: boolean;
  	lockAxis?: "x" | "y";
  }
  export interface AnimationCurve extends AnimationCurveMetadata {
  	keys: IKeyframe[];
  	preWrapMode: WrapMode;
  	postWrapMode: WrapMode;
  }
  const group = "cinematicCamera";
  export interface CinematicCameraCurveModifier {
  	id: string;
  	index: number;
  	curve: AnimationCurve | null;
  	min: number;
  	max: number;
  	groupIndex?: number;
  	curveIndex?: number;
  	children?: Omit<CinematicCameraCurveModifier, "children">[];
  }
  const playbackDuration$: ValueBinding<number>;
  const timelinePosition$: ValueBinding<number>;
  const timelineLength$: ValueBinding<number>;
  const loop$: ValueBinding<boolean>;
  const toggleLoop: (loop: boolean) => void;
  const playing$: ValueBinding<boolean>;
  const setPlaybackDuration: (t: number) => void;
  const onAfterPlaybackDurationChange: () => void;
  const setTimelinePosition: (t: number) => void;
  const stopPlayback: () => void;
  const togglePlayback: () => void;
  const captureKey: (id: string, property: string) => void;
  const removeCameraTransformKey: (curveIndex: number, index: number) => void;
  const moveKeyFrame: (id: string, curveIndex: number, index: number, keyFrame: IKeyframe) => Promise<number>;
  const removeKeyFrame: (id: string, index: number, curveIndex?: number) => void;
  const addKeyFrame: (id: string, time: number, value: number, curveIndex?: number) => Promise<number>;
  const resetCinematicCameraSequence: () => void;
  const getControllerDelta: () => Promise<number[]>;
  const getControllerZoomDelta: () => Promise<number>;
  const getControllerPanDelta: () => Promise<number[]>;
  const toggleCurveEditorFocus: (focus: boolean) => void;
  export interface CinematicCameraAsset {
  	name: string;
  	guid: string;
  	identifier: string;
  	cloudTarget: string;
  	isReadOnly: boolean;
  }
  const saveCinematicCameraSequence: (name: string, hash: string | null) => void;
  const loadCinematicCameraSequence: (hash: string, storage: string) => void;
  const deleteCinematicCameraSequence: (hash: string, storage: string) => void;
  const lastLoadedCinematicCameraSequence$: ValueBinding<CinematicCameraAsset | null>;
  const cinematicCameraSequenceAssets$: ValueBinding<CinematicCameraAsset[]>;
  const availableCloudTargets$: ValueBinding<string[]>;
  const selectedCloudTarget$: ValueBinding<string>;
  const selectCloudTarget: (cloudTarget: string) => void;
  const transformAnimationCurveData$: ValueBinding<CinematicCameraCurveModifier[]>;
  const modifierAnimationCurveData$: ValueBinding<CinematicCameraCurveModifier[]>;
  const useCinematicCameraBindings: (label: string, activeIndex: number) => {
  	onAddKeyframe: (time: number, value: number, curveIndex?: number | undefined) => Promise<number>;
  	onMoveKeyframe: (index: number, keyframe: IKeyframe, meta: {
  		smooth?: boolean;
  		curveIndex?: number;
  		prevKeyframe: IKeyframe;
  	}) => Promise<number>;
  	onRemoveKeyframe: (_index: number, _curveIndex?: number | undefined) => void;
  	onSetKeyframes: (_keyframes: IKeyframe[], _curveIndex?: number | undefined) => never;
  };
  const residentialLowDemand$: ValueBinding<number>;
  const residentialMediumDemand$: ValueBinding<number>;
  const residentialHighDemand$: ValueBinding<number>;
  const commercialDemand$: ValueBinding<number>;
  const industrialDemand$: ValueBinding<number>;
  const officeDemand$: ValueBinding<number>;
  const residentialLowFactors$: ValueBinding<Factor[]>;
  const residentialMediumFactors$: ValueBinding<Factor[]>;
  const residentialHighFactors$: ValueBinding<Factor[]>;
  const commercialFactors$: ValueBinding<Factor[]>;
  const industrialFactors$: ValueBinding<Factor[]>;
  const officeFactors$: ValueBinding<Factor[]>;
  const happiness$: ValueBinding<number>;
  const happinessFactors$: ValueBinding<Factor[]>;
  export interface Factor {
  	factor: string;
  	weight: number;
  }
  const seasonName$: ValueBinding<string | null>;
  const weather$: ValueBinding<WeatherType>;
  const temperature$: ValueBinding<number>;
  export interface Season {
  	name: Name;
  	baseTemperature: number;
  }
  export enum WeatherType {
  	Clear = 0,
  	Few = 1,
  	Scattered = 2,
  	Broken = 3,
  	Overcast = 4,
  	Rain = 5,
  	Snow = 6,
  	Hail = 7,
  	Storm = 8
  }
  /** RGBA color, all values between 0 and 1 */
  export interface Color {
  	readonly r: number;
  	readonly g: number;
  	readonly b: number;
  	readonly a: number;
  }
  export interface Gradient {
  	stops: GradientStop[];
  }
  export interface GradientStop {
  	offset: number;
  	color: string | Color;
  }
  const totalIncome$: ValueBinding<number>;
  const totalExpenses$: ValueBinding<number>;
  const incomeItems$: ValueBinding<BudgetItem[]>;
  const incomeValues$: ValueBinding<number[]>;
  const expenseItems$: ValueBinding<BudgetItem[]>;
  const expenseValues$: ValueBinding<number[]>;
  export interface BudgetItem {
  	id: string;
  	icon: string;
  	color: Color;
  	sources: BudgetSource[];
  	active: boolean;
  }
  export interface BudgetSource {
  	id: string;
  	index: number;
  }
  function getItemValue(item: BudgetItem, values: number[]): number;
  const loanLimit$: ValueBinding<number>;
  const currentLoan$: ValueBinding<Loan>;
  const loanOffer$: ValueBinding<Loan>;
  function requestLoanOffer(amount: number): void;
  function acceptLoanOffer(): void;
  function resetLoanOffer(): void;
  export interface Loan {
  	amount: number;
  	dailyInterestRate: number;
  	dailyPayment: number;
  }
  export enum UISound {
  	selectItem = "select-item",
  	dragSlider = "drag-slider",
  	hoverItem = "hover-item",
  	expandPanel = "expand-panel",
  	grabSlider = "grabSlider",
  	selectDropdown = "select-dropdown",
  	selectToggle = "select-toggle",
  	focusInputField = "focus-input-field",
  	signatureBuildingEvent = "signature-building-event",
  	bulldoze = "bulldoze",
  	bulldozeEnd = "bulldoze-end",
  	relocateBuilding = "relocate-building",
  	mapTilePurchaseMode = "map-tile-purchase-mode",
  	mapTilePurchaseModeEnd = "map-tile-purchase-mode-end",
  	xpEvent = "xp-event",
  	milestoneEvent = "milestone-event",
  	economy = "economy",
  	chirpEvent = "chirp-event",
  	likeChirp = "like-chirp",
  	chirper = "chirper",
  	purchase = "purchase",
  	enableBuilding = "enable-building",
  	disableBuilding = "disable-building",
  	pauseSimulation = "pause-simulation",
  	resumeSimulation = "resume-simulation",
  	simulationSpeed1 = "simulation-speed-1",
  	simulationSpeed2 = "simulation-speed-2",
  	simulationSpeed3 = "simulation-speed-3",
  	togglePolicy = "toggle-policy",
  	takeLoan = "take-loan",
  	removeItem = "remove-item",
  	toggleInfoMode = "toggle-info-mode",
  	takePhoto = "take-photo",
  	tutorialTriggerCompleteEvent = "tutorial-trigger-complete-event",
  	selectRadioNetwork = "select-radio-network",
  	selectRadioStation = "select-radio-station",
  	generateRandomName = "generate-random-name",
  	decreaseElevation = "decrease-elevation",
  	increaseElevation = "increase-elevation",
  	selectPreviousItem = "select-previous-item",
  	selectNextItem = "select-next-item",
  	openPanel = "open-panel",
  	closePanel = "close-panel",
  	openMenu = "open-menu",
  	closeMenu = "close-menu",
  	clickDisableButton = "click-disable-button"
  }
  export class FocusSymbol {
  	readonly debugName: string;
  	readonly r: number;
  	constructor(debugName: string);
  	toString(): string;
  }
  const FOCUS_DISABLED: FocusSymbol;
  const FOCUS_AUTO: FocusSymbol;
  export type FocusKey = typeof FOCUS_DISABLED | typeof FOCUS_AUTO | UniqueFocusKey;
  export type UniqueFocusKey = FocusSymbol | string | number;
  export type Action = () => void | boolean;
  export type Action1D = (value: number) => void | boolean;
  export interface InputActionsDefinition {
  	"Move Horizontal": Action1D;
  	"Change Slider Value": Action1D;
  	"Change Tool Option": Action1D;
  	"Change Value": Action1D;
  	"Change Line Schedule": Action1D;
  	"Select Popup Button": Action1D;
  	"Move Vertical": Action1D;
  	"Switch Radio Station": Action1D;
  	"Scroll Vertical": Action1D;
  	"Scroll Assets": Action1D;
  	"Select": Action;
  	"Purchase Dev Tree Node": Action;
  	"Select Chirp Sender": Action;
  	"Save Game": Action;
  	"Overwrite Save": Action;
  	"Confirm": Action;
  	"Expand Group": Action;
  	"Collapse Group": Action;
  	"Select Route": Action;
  	"Remove Operating District": Action;
  	"Upgrades Menu": Action;
  	"Upgrades Menu Secondary": Action;
  	"Purchase Map Tile": Action;
  	"Unfollow Citizen": Action;
  	"Like Chirp": Action;
  	"Unlike Chirp": Action;
  	"Enable Info Mode": Action;
  	"Disable Info Mode": Action;
  	"Toggle Tool Color Picker": Action;
  	"Cinematic Mode": Action;
  	"Photo Mode": Action;
  	"Focus Citizen": Action;
  	"Unfocus Citizen": Action;
  	"Focus Line Panel": Action;
  	"Focus Occupants Panel": Action;
  	"Focus Info Panel": Action;
  	"Quaternary Action": Action;
  	"Close": Action;
  	"Back": Action;
  	"Leave Underground Mode": Action;
  	"Leave Info View": Action;
  	"Leave Map Tile View": Action;
  	"Jump Section": Action1D;
  	"Switch Tab": Action1D;
  	"Switch Option Section": Action1D;
  	"Switch DLC": Action1D;
  	"Switch Ordering": Action1D;
  	"Switch Radio Network": Action1D;
  	"Change Time Scale": Action1D;
  	"Switch Page": Action1D;
  	"Default Tool": Action;
  	"Default Tool UI": Action;
  	"Tool Options": Action;
  	"Switch Toolmode": Action;
  	"Toggle Snapping": Action;
  	"Toggle Contour Lines": Action;
  	"Capture Keyframe": Action;
  	"Reset Property": Action;
  	"Toggle Property": Action;
  	"Previous Tutorial Phase": Action;
  	"Continue Tutorial": Action;
  	"Finish Tutorial": Action;
  	"Close Tutorial": Action;
  	"Focus Tutorial List": Action;
  	"Start Next Tutorial": Action;
  	"Pause Simulation": Action;
  	"Resume Simulation": Action;
  	"Switch Speed": Action;
  	"Speed 1": Action;
  	"Speed 2": Action;
  	"Speed 3": Action;
  	"Bulldozer": Action;
  	"Exit Underground Mode": Action;
  	"Enter Underground Mode": Action;
  	"Increase Elevation": Action;
  	"Decrease Elevation": Action;
  	"Change Elevation": Action1D;
  	"Advisor": Action;
  	"Quicksave": Action;
  	"Quickload": Action;
  	"Focus Selected Object": Action;
  	"Hide UI": Action;
  	"Map Tile Purchase Panel": Action;
  	"Info View": Action;
  	"Progression Panel": Action;
  	"Economy Panel": Action;
  	"City Information Panel": Action;
  	"Statistic Panel": Action;
  	"Toggle Global Contour Lines": Action;
  	"Transportation Overview Panel": Action;
  	"Notification Panel": Action;
  	"Chirper Panel": Action;
  	"Lifepath Panel": Action;
  	"Universal Mod Panel": Action;
  	"Event Journal Panel": Action;
  	"Radio Panel": Action;
  	"Photo Mode Panel": Action;
  	"Take Photo": Action;
  	"Relocate Selected Object": Action;
  	"Toggle Selected Object Active": Action;
  	"Delete Selected Object": Action;
  	"Toggle Selected Object Emptying": Action;
  	"Toggle Selected Lot Edit": Action;
  	"Toggle Follow Selected Citizen": Action;
  	"Toggle Traffic Routes": Action;
  	"Pause Menu": Action;
  	"Load Game": Action;
  	"Start Game": Action;
  	"Save Options": Action;
  	"Switch User": Action;
  	"Unset Binding": Action;
  	"Reset Binding": Action;
  	"Switch Savegame Location": Action1D;
  	"Show Advanced": Action;
  	"Hide Advanced": Action;
  	"Select Directory": Action;
  	"Search Options": Action;
  	"Clear Search": Action;
  	"Credit Speed": Action1D;
  	"Debug UI": Action;
  	"Debug Prefab Tool": Action;
  	"Debug Change Field": Action1D;
  	"Debug Multiplier": Action1D;
  }
  export type InputAction = keyof InputActionsDefinition;
  export type InputActionName = InputAction | string;
  export enum Unit {
  	Integer = "integer",
  	IntegerRounded = "integerRounded",
  	IntegerPerMonth = "integerPerMonth",
  	IntegerPerHour = "integerPerHour",
  	FloatSingleFraction = "floatSingleFraction",
  	FloatTwoFractions = "floatTwoFractions",
  	FloatThreeFractions = "floatThreeFractions",
  	Percentage = "percentage",
  	PercentageSingleFraction = "percentageSingleFraction",
  	PercentagePrecise = "percentagePrecise",
  	Angle = "angle",
  	Length = "length",
  	Area = "area",
  	Volume = "volume",
  	VolumePerMonth = "volumePerMonth",
  	Weight = "weight",
  	WeightPerCell = "weightPerCell",
  	WeightPerMonth = "weightPerMonth",
  	Power = "power",
  	Energy = "energy",
  	DataRate = "dataRate",
  	DataBytes = "dataBytes",
  	DataMegabytes = "dataMegabytes",
  	Money = "money",
  	MoneyPerCell = "moneyPerCell",
  	MoneyPerMonth = "moneyPerMonth",
  	MoneyPerHour = "moneyPerHour",
  	MoneyPerDistance = "moneyPerDistance",
  	MoneyPerDistancePerMonth = "moneyPerDistancePerMonth",
  	BodiesPerMonth = "bodiesPerMonth",
  	XP = "xp",
  	Temperature = "temperature",
  	TemperaturePrecise = "temperaturePrecise",
  	NetElevation = "netElevation",
  	ScreenFrequency = "screenFrequency",
  	Height = "height",
  	Custom = "custom",
  	DurationSeconds = "durationSeconds"
  }
  export enum LocElementType {
  	Bounds = "Game.UI.Localization.LocalizedBounds",
  	Fraction = "Game.UI.Localization.LocalizedFraction",
  	Number = "Game.UI.Localization.LocalizedNumber",
  	String = "Game.UI.Localization.LocalizedString"
  }
  export interface LocElements {
  	[LocElementType.Bounds]: LocalizedBounds;
  	[LocElementType.Fraction]: LocalizedFraction;
  	[LocElementType.Number]: LocalizedNumber;
  	[LocElementType.String]: LocalizedString;
  }
  export type LocElement = TypeFromMap<LocElements>;
  export interface LocalizedBounds {
  	min: number;
  	max: number;
  	unit?: Unit;
  }
  export interface LocalizedFraction {
  	value: number;
  	total: number;
  	unit?: Unit;
  }
  export interface LocalizedNumber {
  	value: number;
  	unit?: Unit;
  	signed: boolean;
  }
  export interface LocalizedString {
  	id: string | null;
  	value: string | null;
  	args: Record<string, LocElement> | null;
  }
  export enum WidgetType {
  	Column = "Game.UI.Widgets.Column",
  	Row = "Game.UI.Widgets.Row",
  	Scrollable = "Game.UI.Widgets.Scrollable",
  	PageView = "Game.UI.Widgets.PageView",
  	PageLayout = "Game.UI.Widgets.PageLayout",
  	Divider = "Game.UI.Widgets.Divider",
  	Label = "Game.UI.Widgets.Label",
  	MultilineText = "Game.UI.Widgets.MultilineText",
  	Breadcrumbs = "Game.UI.Widgets.Breadcrumbs",
  	Button = "Game.UI.Widgets.Button",
  	ButtonRow = "Game.UI.Widgets.ButtonRow",
  	IconButton = "Game.UI.Widgets.IconButton",
  	IconButtonGroup = "Game.UI.Widgets.IconButtonGroup",
  	Group = "Game.UI.Widgets.Group",
  	ExpandableGroup = "Game.UI.Widgets.ExpandableGroup",
  	PagedList = "Game.UI.Widgets.PagedList",
  	ValueField = "Game.UI.Widgets.ValueField",
  	LocalizedValueField = "Game.UI.Widgets.LocalizedValueField",
  	ToggleField = "Game.UI.Widgets.ToggleField",
  	IntInputField = "Game.UI.Widgets.IntInputField",
  	IntSliderField = "Game.UI.Widgets.IntSliderField",
  	Int2InputField = "Game.UI.Widgets.Int2InputField",
  	Int3InputField = "Game.UI.Widgets.Int3InputField",
  	Int4InputField = "Game.UI.Widgets.Int4InputField",
  	UIntInputField = "Game.UI.Widgets.UIntInputField",
  	UIntSliderField = "Game.UI.Widgets.UIntSliderField",
  	TimeSliderField = "Game.UI.Widgets.TimeSliderField",
  	TimeBoundsSliderField = "Game.UI.Widgets.TimeBoundsSliderField",
  	FloatInputField = "Game.UI.Widgets.FloatInputField",
  	FloatSliderField = "Game.UI.Widgets.FloatSliderField",
  	Float2InputField = "Game.UI.Widgets.Float2InputField",
  	Float2SliderField = "Game.UI.Widgets.Float2SliderField",
  	Float3InputField = "Game.UI.Widgets.Float3InputField",
  	Float3SliderField = "Game.UI.Widgets.Float3SliderField",
  	EulerAnglesField = "Game.UI.Widgets.EulerAnglesField",
  	Float4InputField = "Game.UI.Widgets.Float4InputField",
  	Float4SliderField = "Game.UI.Widgets.Float4SliderField",
  	Bounds1SliderField = "Game.UI.Widgets.Bounds1SliderField",
  	Bounds1InputField = "Game.UI.Widgets.Bounds1InputField",
  	Bounds2InputField = "Game.UI.Widgets.Bounds2InputField",
  	Bounds3InputField = "Game.UI.Widgets.Bounds3InputField",
  	Bezier4x3Field = "Game.UI.Widgets.Bezier4x3Field",
  	RangedSliderField = "Game.UI.Widgets.RangedSliderField",
  	StringInputField = "Game.UI.Widgets.StringInputField",
  	ColorField = "Game.UI.Widgets.ColorField",
  	GradientSliderField = "Game.UI.Widgets.GradientSliderField",
  	AnimationCurveField = "Game.UI.Widgets.AnimationCurveField",
  	EnumField = "Game.UI.Widgets.EnumField",
  	FlagsField = "Game.UI.Widgets.FlagsField",
  	PopupValueField = "Game.UI.Widgets.PopupValueField",
  	DropdownField = "Game.UI.Widgets.DropdownField",
  	DirectoryPickerButton = "Game.UI.Widgets.DirectoryPickerButton",
  	ImageField = "Game.UI.Widgets.ImageField"
  }
  export type PathSegment = string | number;
  export type Path = PathSegment[];
  export interface BaseWidget {
  	disabled?: boolean;
  	hidden?: boolean;
  }
  export interface WidgetIdentifier {
  	group: string;
  	path: Path;
  }
  export interface Named {
  	displayName: LocElement;
  	description?: LocElement;
  }
  export interface TooltipTarget {
  	tooltip?: LocElement | null;
  }
  export interface UITag {
  	uiTag?: string | null;
  }
  export interface WidgetTutorialTarget {
  	tutorialTag: string | null;
  }
  export interface IconButton extends TooltipTarget, WidgetTutorialTarget {
  	icon: string;
  	selected: boolean;
  	disabled: boolean;
  }
  export interface WarningSign {
  	warning?: boolean;
  }
  export enum TooltipPos {
  	Title = 0,
  	Container = 1
  }
  export interface Group extends Named, TooltipTarget, UITag {
  	tooltipPos?: TooltipPos;
  }
  export interface Field<T> extends BaseWidget, Named, TooltipTarget, WidgetTutorialTarget {
  	value: T;
  }
  export interface MinMaxField<T> extends Field<T> {
  	min?: T;
  	max?: T;
  }
  export interface ToggleField extends Field<boolean>, WarningSign {
  }
  export interface IntInputFieldBase<T> extends Field<T> {
  	min?: number;
  	max?: number;
  	step?: number;
  	stepMultiplier?: number;
  }
  export interface IntSliderFieldBase<T> extends Field<T> {
  	min: number;
  	max: number;
  	step?: number;
  	stepMultiplier?: number;
  	unit?: string | null;
  	scaleDragVolume?: boolean;
  	updateOnDragEnd: boolean;
  }
  export type IntInputField = IntInputFieldBase<number>;
  export interface IntSliderField extends IntSliderFieldBase<number>, WarningSign {
  	separateThousands?: boolean;
  	signed?: boolean;
  }
  export interface FloatInputFieldBase<T> extends MinMaxField<T> {
  	fractionDigits?: number;
  	step?: number;
  	stepMultiplier?: number;
  }
  export interface FloatInputField extends FloatInputFieldBase<number> {
  }
  export interface FloatSliderFieldBase<T> extends Field<T> {
  	min: number;
  	max: number;
  	fractionDigits?: number;
  	step?: number;
  	unit?: string | null;
  	scaleDragVolume?: boolean;
  	updateOnDragEnd: boolean;
  }
  export interface FloatSliderField extends FloatSliderFieldBase<number>, WarningSign {
  	separateThousands?: boolean;
  	maxValueWithFraction?: number;
  	signed?: boolean;
  }
  export interface ColorField extends Field<Color> {
  	hdr?: boolean;
  	showAlpha: boolean;
  }
  export interface DropdownField<T> extends Field<T>, WarningSign {
  	items: DropdownItem<T>[];
  }
  export interface DropdownItem<T> {
  	value: T;
  	displayName: LocElement;
  	tooltip?: LocElement;
  	icon?: string;
  	iconTint?: string;
  	disabled?: boolean;
  }
  export interface EnumField extends Field<LongNumber>, WarningSign {
  	enumMembers: EnumMember<LongNumber>[];
  }
  export interface EnumMember<T> extends DropdownItem<T> {
  	value: T;
  }
  export interface Widget<P> {
  	path: string | number;
  	props: P;
  	children: Widget<any>[];
  }
  export type WidgetFromMap<T extends Record<string, any>> = Widget<TypeFromMap<T>>;
  export interface TimeSettings {
  	ticksPerDay: number;
  	daysPerYear: number;
  	epochTicks: number;
  	epochYear: number;
  }
  const timeSettings$: ValueBinding<TimeSettings>;
  const ticks$: ValueBinding<number>;
  const day$: ValueBinding<number>;
  const lightingState$: ValueBinding<LightingState>;
  const simulationPaused$: ValueBinding<boolean>;
  const simulationSpeed$: ValueBinding<number>;
  const simulationPausedBarrier$: EventBinding<boolean>;
  function setSimulationPaused(paused: boolean): void;
  function setSimulationSpeed(speedIndex: number): void;
  export enum LightingState {
  	Dawn = 0,
  	Sunrise = 1,
  	Day = 2,
  	Sunset = 3,
  	Dusk = 4,
  	Night = 5
  }
  export interface SimulationDate {
  	year: number;
  	month: number;
  }
  export interface SimulationTime {
  	hour: number;
  	minute: number;
  }
  export interface SimulationDateTime {
  	year: number;
  	month: number;
  	hour: number;
  	minute: number;
  }
  function dateEquals(a: SimulationDate, b: SimulationDate): boolean;
  function calculateTimeFromMinutesSinceMidnight(minutes: number): SimulationTime;
  function calculateDateFromDays(settings: TimeSettings, days: number): SimulationDate;
  function calculateDateFromTicks(settings: TimeSettings, ticks: number): SimulationDate;
  function calculateDateTimeFromTicks(settings: TimeSettings, ticks: number): SimulationDateTime;
  function calculateMinutesSinceMidnightFromTicks(settings: TimeSettings, ticks: number): number;
  const selectedResource$: {
  	readonly listeners: {
  		listener: BindingListener<Entity> | undefined;
  		set: (listener: BindingListener<Entity>) => void;
  		call: (newValue: Entity) => void;
  	}[];
  	disposed: boolean;
  	_value: Entity;
  	readonly registered: boolean;
  	readonly value: Entity;
  	subscribe: (listener?: BindingListener<Entity> | undefined) => {
  		readonly value: Entity;
  		setChangeListener: (listener: BindingListener<Entity>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: Entity) => void;
  };
  const selectedResourceCategory$: {
  	readonly listeners: {
  		listener: BindingListener<Entity> | undefined;
  		set: (listener: BindingListener<Entity>) => void;
  		call: (newValue: Entity) => void;
  	}[];
  	disposed: boolean;
  	_value: Entity;
  	readonly registered: boolean;
  	readonly value: Entity;
  	subscribe: (listener?: BindingListener<Entity> | undefined) => {
  		readonly value: Entity;
  		setChangeListener: (listener: BindingListener<Entity>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: Entity) => void;
  };
  const maxProgress$: ValueBinding<number>;
  const resourceCategories$: ValueBinding<ResourceCategory[]>;
  const resourceDetails$: MapBinding<Entity, ResourceDetails>;
  const resources$: MapBinding<Entity, Resource>;
  const services$: MapBinding<Entity, Service>;
  const serviceUpkeepConsumption$: MapBinding<Entity, number>;
  const consumptionProduction$: MapBinding<Entity, Number2>;
  const importExport$: MapBinding<Entity, number>;
  const storedResource$: MapBinding<Entity, number>;
  const maxProduction$: MapBinding<Entity, number>;
  const productionChainData$: ValueBinding<ProductionChainData[]>;
  const finalConsumption$: MapBinding<FinalConsumptionQuery, FinalConsumption>;
  export enum FinalConsumer {
  	Citizens = 1,
  	Retail = 3,
  	Commercial = 4,
  	Industrial = 5,
  	Office = 6,
  	Heating = 7,
  	LevelUp = 8,
  	Count = 9
  }
  export interface FinalConsumption {
  	consumer: FinalConsumer;
  	consumption: number;
  }
  export interface FinalConsumptionQuery {
  	resource: Entity;
  	consumer: FinalConsumer;
  }
  export interface ResourceCategory {
  	entity: Entity;
  	name: string;
  	resources: Resource[];
  }
  export interface Resource {
  	entity: Entity;
  	name: string;
  	icon: string;
  	weight: number;
  	tradable: boolean;
  	unit: Unit;
  	producer: ProductionLink;
  	consumers: ProductionLink[];
  }
  export interface ProductionLink {
  	name: string;
  	icon: string;
  }
  export interface ResourceDetails {
  	inputs: Entity[][];
  	outputs: Entity[];
  	serviceOutputs: Entity[];
  }
  export interface ResourceData {
  	production: number;
  	surplus: number;
  	deficit: number;
  }
  export interface Service {
  	entity: Entity;
  	name: string;
  	icon: string;
  }
  export interface ResourceValue {
  	entity: Entity;
  	resource: string;
  	value: number;
  }
  export interface ProductionChainData {
  	consume1: ResourceValue;
  	consume2: ResourceValue;
  	produce: ResourceValue;
  }
  interface Service$1 {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	budget: number;
  }
  export interface ServiceDetails {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	budgetAdjustable: boolean;
  	budgetPercentage: number;
  	efficiency: number;
  	upkeep: number;
  	fees: ServiceFee$1[];
  }
  interface ServiceFee$1 {
  	resource: number;
  	name: string;
  	fee: number;
  	min: number;
  	max: number;
  	adjustable: boolean;
  	importable: boolean;
  	exportable: boolean;
  	incomeInternal: number;
  	incomeExports: number;
  	expenseImports: number;
  	consumptionMultiplier: number;
  	efficiencyMultiplier: number;
  	happinessEffect: number;
  }
  const services$$1: ValueBinding<Service$1[]>;
  const serviceDetails$: MapBinding<Entity, ServiceDetails | null>;
  function setServiceBudget(service: Entity, percentage: number): void;
  function setServiceFee(resource: number, amount: number): void;
  function resetService(service: Entity): void;
  export interface EventInfo {
  	id: string;
  	icon: string;
  	date: number;
  	data: EventData[] | null;
  	effects: EventData[] | null;
  }
  export interface EventData {
  	type: string;
  	value: number;
  }
  const eventMap$: MapBinding<Entity, EventInfo>;
  const events$: ValueBinding<Entity[]>;
  function onOpenJournal(): void;
  function onCloseJournal(): void;
  export enum GameScreen {
  	main = 0,
  	freeCamera = 1,
  	pauseMenu = 10,
  	saveGame = 11,
  	newGame = 12,
  	loadGame = 13,
  	options = 14
  }
  const activeGameScreen$: ValueBinding<GameScreen>;
  const setActiveGameScreen: (screen: GameScreen) => void;
  const canUseSaveSystem$: ValueBinding<boolean>;
  function showMainScreen(): void;
  function showPauseScreen(): void;
  function showFreeCameraScreen(): void;
  export enum LayoutPosition {
  	Undefined = 0,
  	Left = 1,
  	Center = 2,
  	Right = 3
  }
  const activeGamePanel$: ValueBinding<GamePanel | null>;
  const blockingPanelActive$: ValueBinding<boolean>;
  const activePanelPosition$: ValueBinding<LayoutPosition>;
  const toggleGamePanel: (panelType: string) => void;
  const showGamePanel: (panelType: string) => void;
  const closeGamePanel: (panelType: string) => void;
  const closeActiveGamePanel: () => void;
  export enum GamePanelType {
  	InfoviewMenu = "Game.UI.InGame.InfoviewMenu",
  	Progression = "Game.UI.InGame.ProgressionPanel",
  	Economy = "Game.UI.InGame.EconomyPanel",
  	CityInfo = "Game.UI.InGame.CityInfoPanel",
  	Statistics = "Game.UI.InGame.StatisticsPanel",
  	TransportationOverview = "Game.UI.InGame.TransportationOverviewPanel",
  	Chirper = "Game.UI.InGame.ChirperPanel",
  	LifePath = "Game.UI.InGame.LifePathPanel",
  	Journal = "Game.UI.InGame.JournalPanel",
  	Radio = "Game.UI.InGame.RadioPanel",
  	PhotoMode = "Game.UI.InGame.PhotoModePanel",
  	CinematicCamera = "Game.UI.InGame.CinematicCameraPanel",
  	Notifications = "Game.UI.InGame.NotificationsPanel",
  	Glossary = "Game.UI.InGame.GlossaryPanel",
  	ModsMenu = "Game.UI.InGame.ModsMenuPanel"
  }
  export interface GamePanels {
  	[GamePanelType.InfoviewMenu]: InfoviewMenu;
  	[GamePanelType.Progression]: ProgressionPanel;
  	[GamePanelType.Economy]: EconomyPanel;
  	[GamePanelType.CityInfo]: CityInfoPanel;
  	[GamePanelType.Statistics]: StatisticsPanel;
  	[GamePanelType.TransportationOverview]: TransportationOverviewPanel;
  	[GamePanelType.Chirper]: ChirperPanel;
  	[GamePanelType.LifePath]: LifePathPanel;
  	[GamePanelType.Journal]: JournalPanel;
  	[GamePanelType.Radio]: RadioPanel;
  	[GamePanelType.PhotoMode]: PhotoModePanel;
  	[GamePanelType.Notifications]: NotificationsPanel;
  	[GamePanelType.Glossary]: GlossaryPanel;
  	[GamePanelType.ModsMenu]: ModsMenuPanel;
  }
  export type GamePanel = TypeFromMap<GamePanels>;
  function toggleInfoviewMenu(): void;
  export interface InfoviewMenu {
  }
  export interface TabbedGamePanel {
  	selectedTab: number;
  	selectedCategory: number;
  }
  export interface ProgressionPanel extends TabbedGamePanel {
  }
  export enum ProgressionPanelTab {
  	Development = 0,
  	Milestones = 1,
  	Achievements = 2
  }
  const showProgressionPanel: (tab: number) => void;
  export interface EconomyPanel extends TabbedGamePanel {
  }
  export enum EconomyPanelTab {
  	Budget = 0,
  	Loan = 1,
  	Taxation = 2,
  	Services = 3,
  	Production = 4
  }
  const showEconomyPanel: (tab: number) => void;
  export interface CityInfoPanel extends TabbedGamePanel {
  }
  export enum CityInfoPanelTab {
  	Demand = 0,
  	Policies = 1
  }
  const showCityInfoPanel: (tab: number) => void;
  export interface StatisticsPanel {
  }
  export interface TransportationOverviewPanel extends TabbedGamePanel {
  }
  const showTransportationOverviewPanel: (tab: number) => void;
  export enum TransportationOverviewPanelTab {
  	PublicTransport = 0,
  	Cargo = 1
  }
  export interface ChirperPanel {
  }
  export interface LifePathPanel {
  	selectedEntity: Entity;
  }
  function toggleLifePathPanel(): void;
  function showLifePathList(): void;
  const showLifePathDetail: (entity: Entity) => void;
  export interface JournalPanel {
  }
  export interface RadioPanel {
  }
  function toggleRadioPanel(): void;
  function toggleTransportationOverviewPanel(): void;
  export interface PhotoModePanel {
  }
  export interface CinematicCameraPanel {
  }
  export interface NotificationsPanel {
  }
  export interface ModsMenuPanel {
  }
  export interface GlossaryPanel extends TabbedGamePanel {
  }
  export enum GlossaryPanelTab {
  	All = 0,
  	Roads = 1,
  	Zones = 2,
  	Utilities = 3,
  	Services = 4,
  	Transport = 5,
  	Events = 6,
  	Economy = 7,
  	Progression = 8,
  	Tools = 9,
  	Interface = 10
  }
  const showGlossaryPanel: (tab: number) => void;
  const setGlossaryCategory: (category: number) => void;
  export type NumericProperty = NumberProperty | Number2Property | NumberRangeProperty;
  const NUMBER_PROPERTY = "Game.UI.Common.NumberProperty";
  export interface NumberProperty {
  	labelId: string;
  	unit: Unit;
  	value: number;
  	signed: boolean;
  	icon?: string;
  	valueIcon?: string;
  }
  const NUMBER2_PROPERTY = "Game.UI.Common.Number2Property";
  export interface Number2Property {
  	labelId: string;
  	unit: Unit;
  	value: Number2;
  	signed: boolean;
  	icon?: string;
  	valueIcon?: string;
  }
  export interface NumberRangeProperty {
  	labelId: string;
  	unit: Unit;
  	minValue: number;
  	maxValue: number;
  	signed: boolean;
  	icon?: string;
  	valueIcon?: string;
  }
  const STRING_PROPERTY = "Game.UI.Common.StringProperty";
  export interface StringProperty {
  	labelId: string;
  	valueId: string;
  	icon?: string;
  	valueIcon?: string;
  }
  export type Properties = {
  	[NUMBER_PROPERTY]: NumberProperty;
  	[NUMBER2_PROPERTY]: Number2Property;
  	[STRING_PROPERTY]: StringProperty;
  };
  export enum PrefabEffectType {
  	CityModifier = "prefabs.CityModifierEffect",
  	LocalModifier = "prefabs.LocalModifierEffect",
  	LeisureProvider = "prefabs.LeisureProviderEffect",
  	AdjustHappinessEffect = "prefabs.AdjustHappinessEffect"
  }
  export interface PrefabEffects {
  	[PrefabEffectType.CityModifier]: CityModifierEffect;
  	[PrefabEffectType.LocalModifier]: LocalModifierEffect;
  	[PrefabEffectType.LeisureProvider]: LeisureProviderEffect;
  	[PrefabEffectType.AdjustHappinessEffect]: AdjustHappinessEffect;
  }
  export type PrefabEffect = TypeFromMap<PrefabEffects>;
  export interface CityModifierEffect {
  	modifiers: CityModifier[];
  }
  export interface CityModifier {
  	type: CityModifierType;
  	delta: number;
  	unit: Unit;
  }
  export enum CityModifierType {
  	Attractiveness = "Attractiveness",
  	CrimeAccumulation = "CrimeAccumulation",
  	PoliceStationUpkeep = "PoliceStationUpkeep",
  	DisasterWarningTime = "DisasterWarningTime",
  	DisasterDamageRate = "DisasterDamageRate",
  	DiseaseProbability = "DiseaseProbability",
  	ParkEntertainment = "ParkEntertainment",
  	CriminalMonitorProbability = "CriminalMonitorProbability",
  	IndustrialAirPollution = "IndustrialAirPollution",
  	IndustrialGroundPollution = "IndustrialGroundPollution",
  	IndustrialGarbage = "IndustrialGarbage",
  	RecoveryFailChange = "RecoveryFailChange",
  	OreResourceAmount = "OreResourceAmount",
  	OilResourceAmount = "OilResourceAmount",
  	UniversityInterest = "UniversityInterest",
  	OfficeSoftwareDemand = "OfficeSoftwareDemand",
  	IndustrialElectronicsDemand = "IndustrialElectronicsDemand",
  	OfficeSoftwareEfficiency = "OfficeSoftwareEfficiency",
  	IndustrialElectronicsEfficiency = "IndustrialElectronicsEfficiency",
  	TelecomCapacity = "TelecomCapacity",
  	Entertainment = "Entertainment",
  	HighwayTrafficSafety = "HighwayTrafficSafety",
  	PrisonTime = "PrisonTime",
  	CrimeProbability = "CrimeProbability",
  	CollegeGraduation = "CollegeGraduation",
  	UniversityGraduation = "UniversityGraduation",
  	ImportCost = "ImportCost",
  	LoanInterest = "LoanInterest",
  	BuildingLevelingCost = "BuildingLevelingCost",
  	ExportCost = "ExportCost",
  	TaxiStartingFee = "TaxiStartingFee",
  	IndustrialEfficiency = "IndustrialEfficiency",
  	OfficeEfficiency = "OfficeEfficiency",
  	PollutionHealthAffect = "PollutionHealthAffect",
  	HospitalEfficiency = "HospitalEfficiency",
  	IndustrialFishInputEfficiency = "IndustrialFishInputEfficiency",
  	IndustrialFishHubEfficiency = "IndustrialFishHubEfficiency",
  	CityServiceImportCost = "CityServiceImportCost",
  	CityServiceBuildingBaseUpkeepCost = "CityServiceBuildingBaseUpkeepCost",
  	CrimeResponseTime = "CrimeResponseTime",
  	TaxHappiness = "TaxHappiness"
  }
  export interface LocalModifierEffect {
  	modifiers: LocalModifier[];
  }
  export interface LocalModifier {
  	type: LocalModifierType;
  	delta: number;
  	unit: Unit;
  	radius: number;
  }
  export enum LocalModifierType {
  	CrimeAccumulation = "CrimeAccumulation",
  	ForestFireResponseTime = "ForestFireResponseTime",
  	ForestFireHazard = "ForestFireHazard",
  	Wellbeing = "Wellbeing",
  	Health = "Health"
  }
  export interface LeisureProviderEffect {
  	providers: LeisureProvider[];
  }
  export interface LeisureProvider {
  	type: LeisureType;
  	efficiency: number;
  }
  export enum LeisureType {
  	Meals = "Meals",
  	Entertainment = "Entertainment",
  	Commercial = "Commercial",
  	CityIndoors = "CityIndoors",
  	Travel = "Travel",
  	CityPark = "CityPark",
  	CityBeach = "CityBeach",
  	Attractions = "Attractions",
  	Relaxation = "Relaxation",
  	Sightseeing = "Sightseeing"
  }
  export interface AdjustHappinessEffect {
  	targets: string[];
  	wellbeingEffect: number;
  	healthEffect: number;
  }
  const CONSUMPTION_PROPERTY = "prefabs.ConsumptionProperty";
  export interface ConsumptionProperty {
  	electricityConsumption: number;
  	waterConsumption: number;
  	garbageAccumulation: number;
  }
  const POLLUTION_PROPERTY = "prefabs.PollutionProperty";
  export interface PollutionProperty {
  	groundPollution: Pollution;
  	airPollution: Pollution;
  	noisePollution: Pollution;
  }
  export enum Pollution {
  	none = 0,
  	low = 1,
  	medium = 2,
  	high = 3
  }
  const ELECTRICITY_PROPERTY = "prefabs.ElectricityProperty";
  export interface ElectricityProperty {
  	labelId: string;
  	minCapacity: number;
  	maxCapacity: number;
  	voltage: Voltage;
  }
  export enum Voltage {
  	low = 0,
  	high = 1,
  	both = 2
  }
  const TRANSPORT_STOP_PROPERTY = "prefabs.TransportStopProperty";
  export interface TransportStopProperty {
  	stops: {
  		[key: string]: number;
  	};
  }
  const UPKEEPNUMBER_PROPERTY = "prefabs.UpkeepIntProperty";
  export interface UpkeepNumberProperty {
  	labelId: string;
  	unit: Unit;
  	value: number;
  	signed: boolean;
  }
  const UPKEEPNUMBER2_PROPERTY = "prefabs.UpkeepInt2Property";
  export interface UpkeepNumber2Property {
  	labelId: string;
  	unit: Unit;
  	value: Number2;
  	signed: boolean;
  }
  export interface PrefabProperties extends Properties {
  	[UPKEEPNUMBER_PROPERTY]: UpkeepNumberProperty;
  	[UPKEEPNUMBER2_PROPERTY]: UpkeepNumber2Property;
  	[CONSUMPTION_PROPERTY]: ConsumptionProperty;
  	[POLLUTION_PROPERTY]: PollutionProperty;
  	[ELECTRICITY_PROPERTY]: ElectricityProperty;
  	[TRANSPORT_STOP_PROPERTY]: TransportStopProperty;
  }
  export type PrefabProperty = TypeFromMap<PrefabProperties>;
  export interface PrefabRequirementBase {
  	entity: Entity;
  	locked: boolean;
  }
  export interface MilestoneRequirement extends PrefabRequirementBase {
  	index: number;
  }
  export interface DevTreeNodeRequirement extends PrefabRequirementBase {
  	name: string;
  }
  export interface StrictObjectBuiltRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  	icon: string;
  	requirement: string;
  	minimumCount: number;
  	isUpgrade: boolean;
  }
  export interface ZoneBuiltRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  	icon: string;
  	requiredTheme: string | null;
  	requiredZone: string | null;
  	requiredType: AreaType;
  	minimumSquares: number;
  	minimumCount: number;
  	minimumLevel: number;
  }
  export enum AreaType {
  	none = 0,
  	residential = 1,
  	commercial = 2,
  	industrial = 3
  }
  export interface CitizenRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  	minimumPopulation: number;
  	minimumHappiness: number;
  }
  export interface ProcessingRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  	icon: string;
  	resourceType: string;
  	minimumProducedAmount: number;
  }
  export interface TransportRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  	icon: string;
  	name: string;
  	transportType: string;
  	filterID: string;
  	minimumTransportedCargo: number;
  	minimumTransportedPassenger: number;
  }
  export interface ObjectBuiltRequirement extends UnlockRequirement {
  	name: string;
  	minimumCount: number;
  }
  export interface UnlockRequirement extends PrefabRequirementBase {
  	labelId: string | null;
  	progress: number;
  }
  export interface TutorialRequirement extends PrefabRequirementBase {
  }
  export enum PrefabRequirementType {
  	Milestone = "prefabs.MilestoneRequirement",
  	DevTreeNode = "prefabs.DevTreeNodeRequirement",
  	StrictObjectBuilt = "prefabs.StrictObjectBuiltRequirement",
  	ZoneBuilt = "prefabs.ZoneBuiltRequirement",
  	Citizen = "prefabs.CitizenRequirement",
  	Processing = "prefabs.ProcessingRequirement",
  	Transport = "prefabs.TransportRequirement",
  	ObjectBuilt = "prefabs.ObjectBuiltRequirement",
  	Unlock = "prefabs.UnlockRequirement",
  	Tutorial = "prefabs.TutorialRequirement",
  	PrefabUnlocked = "prefabs.PrefabUnlockedRequirement"
  }
  export interface PrefabRequirements {
  	[PrefabRequirementType.Milestone]: MilestoneRequirement;
  	[PrefabRequirementType.DevTreeNode]: DevTreeNodeRequirement;
  	[PrefabRequirementType.StrictObjectBuilt]: StrictObjectBuiltRequirement;
  	[PrefabRequirementType.ZoneBuilt]: ZoneBuiltRequirement;
  	[PrefabRequirementType.Citizen]: CitizenRequirement;
  	[PrefabRequirementType.Processing]: ProcessingRequirement;
  	[PrefabRequirementType.Transport]: TransportRequirement;
  	[PrefabRequirementType.ObjectBuilt]: ObjectBuiltRequirement;
  	[PrefabRequirementType.Unlock]: UnlockRequirement;
  	[PrefabRequirementType.Tutorial]: TutorialRequirement;
  	[PrefabRequirementType.PrefabUnlocked]: UnlockRequirement;
  }
  export type PrefabRequirement = TypeFromMap<PrefabRequirements>;
  export interface Theme {
  	entity: Entity;
  	name: string;
  	icon: string;
  }
  export interface UnlockingRequirements {
  	requireAny: PrefabRequirement[];
  	requireAll: PrefabRequirement[];
  }
  export interface PrefabDetails {
  	entity: Entity;
  	name: string;
  	uiTag: string;
  	icon: string;
  	dlc: string | null;
  	preview: string | null;
  	titleId: string;
  	descriptionId: string | null;
  	locked: boolean;
  	unique: boolean;
  	placed: boolean;
  	constructionCost: NumericProperty | null;
  	effects: PrefabEffect[];
  	properties: PrefabProperty[];
  	requirements: UnlockingRequirements;
  }
  const themes$: ValueBinding<Theme[]>;
  const prefabDetails$: MapBinding<Entity, PrefabDetails | null>;
  const manualUITags$: ValueBinding<ManualUITagsConfiguration | null>;
  const emptyPrefabDetails: PrefabDetails;
  export interface ManualUITagsConfiguration {
  	chirperPanel: string;
  	chirperPanelButton: string;
  	chirperPanelChirps: string;
  	cityInfoPanel: string;
  	cityInfoPanelButton: string;
  	cityInfoPanelDemandPage: string;
  	cityInfoPanelDemandTab: string;
  	cityInfoPanelPoliciesPage: string;
  	cityInfoPanelPoliciesTab: string;
  	economyPanelBudgetBalance: string;
  	economyPanelBudgetExpenses: string;
  	economyPanelBudgetPage: string;
  	economyPanelBudgetRevenue: string;
  	economyPanelBudgetTab: string;
  	economyPanelButton: string;
  	economyPanelLoansAccept: string;
  	economyPanelLoansPage: string;
  	economyPanelLoansSlider: string;
  	economyPanelLoansTab: string;
  	economyPanelProductionPage: string;
  	economyPanelProductionResources: string;
  	economyPanelProductionDiagram: string;
  	economyPanelProductionData: string;
  	economyPanelProductionTab: string;
  	economyPanelServicesBudget: string;
  	economyPanelServicesList: string;
  	economyPanelServicesPage: string;
  	economyPanelServicesTab: string;
  	economyPanelTaxationEstimate: string;
  	economyPanelTaxationPage: string;
  	economyPanelTaxationRate: string;
  	economyPanelTaxationTab: string;
  	economyPanelTaxationType: string;
  	eventJournalPanel: string;
  	eventJournalPanelButton: string;
  	infoviewsButton: string;
  	infoviewsMenu: string;
  	infoviewsPanel: string;
  	infoviewsFireHazard: string;
  	lifePathPanel: string;
  	lifePathPanelBackButton: string;
  	lifePathPanelButton: string;
  	lifePathPanelChirps: string;
  	lifePathPanelDetail: string;
  	mapTilePanel: string;
  	mapTilePanelButton: string;
  	mapTilePanelResources: string;
  	mapTilePanelPurchase: string;
  	photoModePanel: string;
  	photoModePanelButton: string;
  	photoModePanelHideUI: string;
  	photoModePanelTakePicture: string;
  	photoModeTab: string;
  	photoModePanelTitle: string;
  	photoModeCinematicCameraToggle: string;
  	cinematicCameraPanel: string;
  	cinematicCameraPanelCaptureKey: string;
  	cinematicCameraPanelPlay: string;
  	cinematicCameraPanelStop: string;
  	cinematicCameraPanelHideUI: string;
  	cinematicCameraPanelSaveLoad: string;
  	cinematicCameraPanelReset: string;
  	cinematicCameraPanelTimelineSlider: string;
  	cinematicCameraPanelTransformCurves: string;
  	cinematicCameraPanelPropertyCurves: string;
  	cinematicCameraPanelPlaybackDurationSlider: string;
  	progressionPanel: string;
  	progressionPanelButton: string;
  	progressionPanelDevelopmentNode: string;
  	progressionPanelDevelopmentPage: string;
  	progressionPanelDevelopmentService: string;
  	progressionPanelDevelopmentTab: string;
  	progressionPanelDevelopmentUnlockableNode: string;
  	progressionPanelDevelopmentUnlockNode: string;
  	progressionPanelMilestoneRewards: string;
  	progressionPanelMilestoneRewardsMoney: string;
  	progressionPanelMilestoneRewardsDevPoints: string;
  	progressionPanelMilestoneRewardsMapTiles: string;
  	progressionPanelMilestonesList: string;
  	progressionPanelMilestonesPage: string;
  	progressionPanelMilestonesTab: string;
  	progressionPanelMilestoneXP: string;
  	radioPanel: string;
  	radioPanelAdsToggle: string;
  	radioPanelButton: string;
  	radioPanelNetworks: string;
  	radioPanelStations: string;
  	radioPanelVolumeSlider: string;
  	statisticsPanel: string;
  	statisticsPanelButton: string;
  	statisticsPanelMenu: string;
  	statisticsPanelTimeScale: string;
  	toolbarBulldozerBar: string;
  	toolbarDemand: string;
  	toolbarSimulationDateTime: string;
  	toolbarSimulationSpeed: string;
  	toolbarSimulationToggle: string;
  	toolbarUnderground: string;
  	toolOptions: string;
  	toolOptionsBrushSize: string;
  	toolOptionsBrushStrength: string;
  	toolOptionsElevation: string;
  	toolOptionsElevationDecrease: string;
  	toolOptionsElevationIncrease: string;
  	toolOptionsElevationStep: string;
  	toolOptionsModes: string;
  	toolOptionsVegatationAge: string;
  	toolOptionsModesComplexCurve: string;
  	toolOptionsModesContinuous: string;
  	toolOptionsModesGrid: string;
  	toolOptionsModesReplace: string;
  	toolOptionsModesSimpleCurve: string;
  	toolOptionsModesStraight: string;
  	toolOptionsParallelMode: string;
  	toolOptionsParallelModeOffset: string;
  	toolOptionsParallelModeOffsetDecrease: string;
  	toolOptionsParallelModeOffsetIncrease: string;
  	toolOptionsSnapping: string;
  	toolOptionsThemes: string;
  	toolOptionsAssetPacks: string;
  	toolOptionsUnderground: string;
  	toolOptionsWaterShowSourceNames: string;
  	toolOptionsWaterSimulateBackdrop: string;
  	toolOptionsWaterSimSpeed: string;
  	transportationOverviewPanel: string;
  	transportationOverviewPanelButton: string;
  	transportationOverviewPanelLegend: string;
  	transportationOverviewPanelLines: string;
  	transportationOverviewPanelTabCargo: string;
  	transportationOverviewPanelTabPublicTransport: string;
  	transportationOverviewPanelTransportTypes: string;
  	selectedInfoPanel: string;
  	selectedInfoPanelTitle: string;
  	selectedInfoPanelPolicies: string;
  	selectedInfoPanelDelete: string;
  	pauseMenuButton: string;
  	upgradeGrid: string;
  	assetGrid: string;
  	actionHints: string;
  	assetImportButton: string;
  	editorInfoViewsPanel: string;
  	editorCameraControls: string;
  	resetTODButton: string;
  	simulationPlayButton: string;
  	tutorialsToggle: string;
  	editorPauseMenuButton: string;
  	workspaceTitleBar: string;
  	bulldozerButton: string;
  	waterSettingsTitle: string;
  	selectProjectRoot: string;
  	selectAssets: string;
  	selectTemplate: string;
  	importButton: string;
  	addObjectButton: string;
  	filterMenu: string;
  	favoriteStar: string;
  	tutorialListPanel: string;
  	glossaryPanelAllTab: string;
  	glossaryPanelZonesTab: string;
  	glossaryPanelUtilitiesTab: string;
  	glossaryPanelRoadsTabs: string;
  	glossaryPanelTransportTab: string;
  	glossaryPanelEventsTab: string;
  	glossaryPanelProgressionTab: string;
  	glossaryPanelToolsTab: string;
  	glossaryPanelEconomyTab: string;
  	glossaryPanelInterfaceTab: string;
  	glossaryPanelServicesTab: string;
  	universalModPanel: string;
  	universalModPanelButton: string;
  }
  export interface Infoview {
  	entity: Entity;
  	id: string;
  	icon: string | null;
  	locked: boolean;
  	uiTag: string;
  	group: number;
  	editor: boolean;
  	requirements: UnlockingRequirements;
  }
  export interface ActiveInfoview {
  	entity: Entity;
  	id: string;
  	icon: string;
  	uiTag: string;
  	infomodes: Infomode[];
  	editor: boolean;
  }
  export interface Infomode {
  	entity: Entity;
  	id: string;
  	editor: boolean;
  	uiTag: string;
  	active: boolean;
  	priority: number;
  	color: Color | null;
  	gradientLegend: InfomodeGradientLegend | null;
  	colorLegends: InfomodeColorLegend[];
  	type: string;
  }
  export interface InfomodeGradientLegend {
  	lowLabel: LocElement | null;
  	highLabel: LocElement | null;
  	gradient: Gradient;
  }
  export interface InfomodeColorLegend {
  	color: Color;
  	label: LocElement;
  }
  export interface ZoneInfo {
  	category: string;
  	color: Color;
  	edge: Color;
  	fillAlpha: number;
  	edgeAlpha: number;
  }
  const infoviews$: ValueBinding<Infoview[]>;
  const activeInfoview$: ValueBinding<ActiveInfoview | null>;
  const zoneInfos$: ValueBinding<ZoneInfo[]>;
  function closeInfoviewMenu(): void;
  function setActiveInfoview(entity: Entity): void;
  function clearActiveInfoview(): void;
  function setInfomodeActive(entity: Entity, active: boolean, priority: number): void;
  function setZoneAlpha(category: string, fillAlpha: number, edgeAlpha: number): void;
  const electricityConsumption$: ValueBinding<number>;
  const electricityProduction$: ValueBinding<number>;
  const electricityTransmitted$: ValueBinding<number>;
  const electricityExport$: ValueBinding<number>;
  const electricityImport$: ValueBinding<number>;
  const electricityAvailability$: ValueBinding<IndicatorValue>;
  const electricityTransmission$: ValueBinding<IndicatorValue>;
  const electricityTrade$: ValueBinding<IndicatorValue>;
  const batteryCharge$: ValueBinding<IndicatorValue>;
  const waterCapacity$: ValueBinding<number>;
  const waterConsumption$: ValueBinding<number>;
  const sewageCapacity$: ValueBinding<number>;
  const sewageConsumption$: ValueBinding<number>;
  const waterExport$: ValueBinding<number>;
  const waterImport$: ValueBinding<number>;
  const sewageExport$: ValueBinding<number>;
  const sewageAvailability$: ValueBinding<IndicatorValue>;
  const waterAvailability$: ValueBinding<IndicatorValue>;
  const waterTrade$: ValueBinding<IndicatorValue>;
  const averageWealth$: ValueBinding<string>;
  const averageIncome$: ValueBinding<number>;
  const averageRent$: ValueBinding<number>;
  const averageUpkeep$: ValueBinding<number>;
  const averageResourceCost$: ValueBinding<number>;
  const averageFees$: ValueBinding<number>;
  const elementaryEligible$: ValueBinding<number>;
  const highSchoolEligible$: ValueBinding<number>;
  const collegeEligible$: ValueBinding<number>;
  const universityEligible$: ValueBinding<number>;
  const elementaryCapacity$: ValueBinding<number>;
  const highSchoolCapacity$: ValueBinding<number>;
  const collegeCapacity$: ValueBinding<number>;
  const universityCapacity$: ValueBinding<number>;
  const educationData$: ValueBinding<ChartData>;
  const elementaryStudents$: ValueBinding<number>;
  const highSchoolStudents$: ValueBinding<number>;
  const collegeStudents$: ValueBinding<number>;
  const universityStudents$: ValueBinding<number>;
  const elementaryAvailability$: ValueBinding<IndicatorValue>;
  const highSchoolAvailability$: ValueBinding<IndicatorValue>;
  const collegeAvailability$: ValueBinding<IndicatorValue>;
  const universityAvailability$: ValueBinding<IndicatorValue>;
  const transportSummaries$: ValueBinding<TransportSummaries>;
  const averageHealth$: ValueBinding<number>;
  const cemeteryUse$: ValueBinding<number>;
  const cemeteryCapacity$: ValueBinding<number>;
  const deathRate$: ValueBinding<number>;
  const processingRate$: ValueBinding<number>;
  const sickCount$: ValueBinding<number>;
  const patientCount$: ValueBinding<number>;
  const patientCapacity$: ValueBinding<number>;
  const healthcareAvailability$: ValueBinding<IndicatorValue>;
  const deathcareAvailability$: ValueBinding<IndicatorValue>;
  const cemeteryAvailability$: ValueBinding<IndicatorValue>;
  const garbageProcessingRate$: ValueBinding<number>;
  const landfillCapacity$: ValueBinding<number>;
  const processingAvailability$: ValueBinding<IndicatorValue>;
  const landfillAvailability$: ValueBinding<IndicatorValue>;
  const garbageProductionRate$: ValueBinding<number>;
  const storedGarbage$: ValueBinding<number>;
  const parkingCapacity$: ValueBinding<number>;
  const parkingIncome$: ValueBinding<number>;
  const parkedCars$: ValueBinding<number>;
  const parkingAvailability$: ValueBinding<IndicatorValue>;
  const bikeParkingAvailability$: ValueBinding<IndicatorValue>;
  const bikeParking$: ValueBinding<Number2>;
  const trafficFlow$: ValueBinding<number[]>;
  const averageGroundPollution$: ValueBinding<IndicatorValue>;
  const averageAirPollution$: ValueBinding<IndicatorValue>;
  const averageWaterPollution$: ValueBinding<IndicatorValue>;
  const averageNoisePollution$: ValueBinding<IndicatorValue>;
  const averageFireHazard$: ValueBinding<IndicatorValue>;
  const averageCrimeProbability$: ValueBinding<IndicatorValue>;
  const jailAvailability$: ValueBinding<IndicatorValue>;
  const prisonAvailability$: ValueBinding<IndicatorValue>;
  const crimeProducers$: ValueBinding<number>;
  const crimeProbability$: ValueBinding<number>;
  const jailCapacity$: ValueBinding<number>;
  const arrestedCriminals$: ValueBinding<number>;
  const inJail$: ValueBinding<number>;
  const prisonCapacity$: ValueBinding<number>;
  const prisoners$: ValueBinding<number>;
  const inPrison$: ValueBinding<number>;
  const criminals$: ValueBinding<number>;
  const crimePerMonth$: ValueBinding<number>;
  const escapedRate$: ValueBinding<number>;
  const averageLandValue$: ValueBinding<number>;
  const residentialLevels$: ValueBinding<ChartData>;
  const commercialLevels$: ValueBinding<ChartData>;
  const industrialLevels$: ValueBinding<ChartData>;
  const officeLevels$: ValueBinding<ChartData>;
  const shelteredCount$: ValueBinding<number>;
  const shelterCapacity$: ValueBinding<number>;
  const shelterAvailability$: ValueBinding<IndicatorValue>;
  const attractiveness$: ValueBinding<IndicatorValue>;
  const averageHotelPrice$: ValueBinding<number>;
  const tourismRate$: ValueBinding<number>;
  const weatherEffect$: ValueBinding<number>;
  const mailProductionRate$: ValueBinding<number>;
  const collectedMail$: ValueBinding<number>;
  const deliveredMail$: ValueBinding<number>;
  const postServiceAvailability$: ValueBinding<IndicatorValue>;
  const population$: ValueBinding<number>;
  const employed$: ValueBinding<number>;
  const jobs$: ValueBinding<number>;
  const unemployment$: ValueBinding<number>;
  const birthRate$: ValueBinding<number>;
  const homeless$: ValueBinding<number>;
  const homelessness$: ValueBinding<number>;
  const movedIn$: ValueBinding<number>;
  const movedAway$: ValueBinding<number>;
  const ageData$: ValueBinding<ChartData>;
  const commercialProfitability$: ValueBinding<IndicatorValue>;
  const industrialProfitability$: ValueBinding<IndicatorValue>;
  const officeProfitability$: ValueBinding<IndicatorValue>;
  const topImportNames$: ValueBinding<string[]>;
  const topImportColors$: ValueBinding<string[]>;
  const topImportData$: ValueBinding<ChartData>;
  const topExportNames$: ValueBinding<string[]>;
  const topExportColors$: ValueBinding<string[]>;
  const topExportData$: ValueBinding<ChartData>;
  const availableOil$: ValueBinding<number>;
  const availableOre$: ValueBinding<number>;
  const availableForest$: ValueBinding<number>;
  const availableFertility$: ValueBinding<number>;
  const oilExtractionRate$: ValueBinding<number>;
  const oreExtractionRate$: ValueBinding<number>;
  const forestExtractionRate$: ValueBinding<number>;
  const fertilityExtractionRate$: ValueBinding<number>;
  const forestRenewalRate$: ValueBinding<number>;
  const fertilityRenewalRate$: ValueBinding<number>;
  const availableFish$: ValueBinding<number>;
  const fishExtractionRate$: ValueBinding<number>;
  const fishRenewalRate$: ValueBinding<number>;
  const workplacesData$: ValueBinding<ChartData>;
  const employeesData$: ValueBinding<ChartData>;
  const worksplaces$: ValueBinding<number>;
  const workers$: ValueBinding<number>;
  export interface IndicatorValue {
  	min: number;
  	max: number;
  	current: number;
  }
  export interface ChartData {
  	values: number[];
  	total: number;
  }
  export interface TransportSummaries {
  	passengerSummaries: PassengerSummary[];
  	cargoSummaries: CargoSummary[];
  }
  export interface PassengerSummary {
  	id: string;
  	icon: string;
  	locked: boolean;
  	lineCount: number;
  	touristCount: number;
  	citizenCount: number;
  	requirements: UnlockingRequirements;
  }
  export interface CargoSummary {
  	id: string;
  	icon: string;
  	locked: boolean;
  	lineCount: number;
  	cargoCount: number;
  	requirements: UnlockingRequirements;
  }
  function useInfoviewToggle(infoviewId: string): (() => void) | undefined;
  export interface NotificationData {
  	key: string;
  	count: number;
  	iconPath: string;
  }
  export interface FollowedCitizen {
  	entity: Entity;
  	name: Name;
  	age: string;
  }
  export interface LifePathDetails {
  	entity: Entity;
  	name: Name;
  	birthDay: number;
  	age: string;
  	education: string;
  	wealth: string;
  	occupation: string;
  	jobLevel: string;
  	residenceName: Name | null;
  	residenceEntity: Entity | null;
  	residenceKey: string;
  	workplaceName: Name | null;
  	workplaceEntity: Entity | null;
  	workplaceKey: string;
  	schoolName: Name | null;
  	schoolEntity: Entity | null;
  	conditions: NotificationData[];
  	happiness: NotificationData | null;
  	state: string;
  }
  export interface LifePathEvent {
  	entity: Entity;
  	date: number;
  	messageId: string;
  }
  export enum LifePathItemType {
  	Chirp = "chirper.Chirp",
  	LogEntry = "lifePath.LifePathEvent"
  }
  export interface LifePathItems {
  	[LifePathItemType.Chirp]: Chirp;
  	[LifePathItemType.LogEntry]: LifePathEvent;
  }
  export type LifePathItem = TypeFromMap<LifePathItems>;
  const followedCitizens$: ValueBinding<FollowedCitizen[]>;
  const lifePathDetails$: MapBinding<Entity, LifePathDetails | null>;
  const lifePathItems$: MapBinding<Entity, (LifePathItem | null)[]>;
  const maxFollowedCitizens$: ValueBinding<number>;
  function followCitizen(citizen: Entity): void;
  function unfollowCitizen(citizen: Entity): void;
  const mapTilePanelVisible$: ValueBinding<boolean>;
  const mapTileViewActive$: ValueBinding<boolean>;
  const buildableLand$: ValueBinding<MapTileResource>;
  const availableWater$: ValueBinding<MapTileResource>;
  const resources$$1: ValueBinding<MapTileResource[]>;
  const purchasePrice$: ValueBinding<number>;
  const purchaseUpkeep$: ValueBinding<number>;
  const purchaseFlags$: ValueBinding<number>;
  const permits$: ValueBinding<number>;
  const permitCost$: ValueBinding<number>;
  function setMapTileViewActive(enabled: boolean): void;
  function disableMapTileView(): void;
  function purchaseMapTiles(): void;
  export interface MapTileResource {
  	id: string;
  	icon: string;
  	value: number;
  	unit: Unit;
  }
  export enum MapTileStatus {
  	None = 0,
  	NoCurrentlyAvailable = 1,
  	NoAvailable = 2,
  	NoSelection = 4,
  	InsufficientFunds = 8,
  	InsufficientPermits = 16
  }
  const group$1 = "photoMode";
  function resetCamera(): void;
  const overlayHidden$: ValueBinding<boolean>;
  function setOverlayHidden(overlayHidden: boolean): void;
  const orbitCameraActive$: ValueBinding<boolean>;
  function toggleOrbitCameraActive(): void;
  function takeScreenshot(): void;
  export interface Tab {
  	id: string;
  	icon: string;
  }
  function selectTab(tab: string): void;
  function toggleCinematicCamera(visible: boolean): void;
  const cinematicCameraVisible$: ValueBinding<boolean>;
  const tabs$: ValueBinding<Tab[]>;
  const selectedTab$: ValueBinding<string>;
  export enum PhotoWidgetType {
  	DropdownField = "Game.UI.Widgets.DropdownField"
  }
  export interface PhotoModeWidgets {
  	[WidgetType.FloatSliderField]: FloatSliderField;
  	[WidgetType.IntSliderField]: IntSliderField;
  	[WidgetType.Group]: Group;
  	[WidgetType.ToggleField]: ToggleField;
  	[WidgetType.FloatInputField]: FloatInputField;
  	[WidgetType.IntInputField]: IntInputField;
  	[WidgetType.IconButton]: IconButton;
  	[WidgetType.EnumField]: EnumField;
  	[PhotoWidgetType.DropdownField]: DropdownField<any>;
  	[WidgetType.ColorField]: ColorField;
  }
  export type PhotoModeWidget = WidgetFromMap<PhotoModeWidgets>;
  const root: WidgetIdentifier;
  const adjustments$: ValueBinding<PhotoModeWidget[] | null>;
  const cityPolicies$: ValueBinding<PolicyData[]>;
  function setCityPolicy(entity: Entity, active: boolean, value?: number): void;
  function setPolicy(entity: Entity, active: boolean, value?: number): void;
  export interface PolicyData {
  	id: string;
  	icon: string;
  	entity: Entity;
  	active: boolean;
  	locked: boolean;
  	uiTag: string;
  	requirements: UnlockingRequirements;
  	data: PolicySliderData | null;
  }
  export interface PolicySliderData {
  	value: number;
  	range: Bounds1;
  	default: number;
  	step: number;
  	unit: Unit;
  }
  export interface PolicySliderProp {
  	sliderData: PolicySliderData;
  }
  export interface DevTreeService {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	uiTag: string;
  	requirements: UnlockingRequirements;
  }
  export interface DevTreeServiceDetails {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	milestoneRequirement: number;
  }
  export interface DevTreeNode {
  	entity: Entity;
  	name: string;
  	icon: string;
  	cost: number;
  	locked: boolean;
  	position: Number2;
  	requirements: Entity[];
  	unlockable: boolean;
  }
  export interface DevTreeNodeDetails {
  	entity: Entity;
  	name: string;
  	icon: string;
  	cost: number;
  	locked: boolean;
  	unlockable: boolean;
  	requirementCount: number;
  	milestoneRequirement: number;
  }
  const devPoints$: ValueBinding<number>;
  const services$$2: ValueBinding<DevTreeService[]>;
  const serviceDetails$$1: MapBinding<Entity, DevTreeServiceDetails | null>;
  const nodes$: MapBinding<Entity, DevTreeNode[]>;
  const nodeDetails$: MapBinding<Entity, DevTreeNodeDetails | null>;
  const selectedDevTree$: {
  	readonly listeners: {
  		listener: BindingListener<Entity> | undefined;
  		set: (listener: BindingListener<Entity>) => void;
  		call: (newValue: Entity) => void;
  	}[];
  	disposed: boolean;
  	_value: Entity;
  	readonly registered: boolean;
  	readonly value: Entity;
  	subscribe: (listener?: BindingListener<Entity> | undefined) => {
  		readonly value: Entity;
  		setChangeListener: (listener: BindingListener<Entity>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: Entity) => void;
  };
  const selectedNode$: {
  	readonly listeners: {
  		listener: BindingListener<Entity> | undefined;
  		set: (listener: BindingListener<Entity>) => void;
  		call: (newValue: Entity) => void;
  	}[];
  	disposed: boolean;
  	_value: Entity;
  	readonly registered: boolean;
  	readonly value: Entity;
  	subscribe: (listener?: BindingListener<Entity> | undefined) => {
  		readonly value: Entity;
  		setChangeListener: (listener: BindingListener<Entity>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: Entity) => void;
  };
  function purchaseNode(node: Entity): void;
  const lockedFeatures$: ValueBinding<LockedFeature[]>;
  export interface LockedFeature {
  	name: string;
  	requirements: UnlockingRequirements;
  }
  export interface UnlockingProps {
  	locked: boolean;
  	requirements: UnlockingRequirements;
  }
  function useFeatureUnlocking(feature: string): UnlockingProps;
  function useFeatureLocked(feature: string): boolean;
  function isFeatureLocked(feature: string): boolean;
  export interface XpMessage {
  	amount: number;
  	reason: string;
  }
  export interface Milestone {
  	entity: Entity;
  	index: number;
  	major: boolean;
  	locked: boolean;
  	isVictory?: boolean;
  }
  export interface MilestoneDetails {
  	entity: Entity;
  	index: number;
  	xpRequirement: number;
  	reward: number;
  	devTreePoints: number;
  	mapTiles: number;
  	loanLimit: number;
  	image: string;
  	backgroundColor: Color;
  	accentColor: Color;
  	textColor: Color;
  	locked: boolean;
  	isVictory: boolean;
  }
  const defaultMilestoneDetails: MilestoneDetails;
  export interface Feature {
  	entity: Entity;
  	icon: string;
  	name: string;
  }
  interface Service$2 {
  	entity: Entity;
  	icon: string;
  	name: string;
  	devTreeUnlocked: boolean;
  	assets: Asset[];
  }
  export enum MilestoneUnlockType {
  	Feature = "milestone.Feature",
  	Service = "milestone.Service",
  	Zone = "milestone.Asset",
  	Policy = "milestone.Policy"
  }
  export interface MilestoneUnlocks {
  	[MilestoneUnlockType.Feature]: Feature;
  	[MilestoneUnlockType.Service]: Service$2;
  	[MilestoneUnlockType.Zone]: Asset;
  	[MilestoneUnlockType.Policy]: Policy;
  }
  export type MilestoneUnlock = TypeFromMap<MilestoneUnlocks>;
  export interface Asset {
  	entity: Entity;
  	name: string;
  	icon: string;
  	themes: Entity[];
  }
  export interface Policy {
  	entity: Entity;
  	name: string;
  	icon: string;
  }
  export interface UnlockDetails {
  	entity: Entity;
  	icon: string;
  	titleId: string;
  	descriptionId: string;
  	locked: boolean;
  	hasDevTree: boolean;
  }
  const achievedMilestone$: ValueBinding<number>;
  const achievedMilestoneXP$: ValueBinding<number>;
  const nextMilestoneXP$: ValueBinding<number>;
  const totalXP$: ValueBinding<number>;
  const xpMessageAdded$: EventBinding<XpMessage>;
  const maxMilestoneReached$: ValueBinding<boolean>;
  const milestones$: ValueBinding<Milestone[]>;
  const unlockedMilestone$: ValueBinding<Entity>;
  function clearUnlockedMilestone(): void;
  const milestoneDetails$: MapBinding<Entity, MilestoneDetails | null>;
  const milestoneUnlocks$: MapBinding<Entity, MilestoneUnlock[]>;
  const unlockDetails$: MapBinding<Entity, UnlockDetails | null>;
  const unlockAll$: ValueBinding<boolean>;
  const reachedPopulationGoal$: ValueBinding<boolean>;
  const victoryPopupShown$: ValueBinding<boolean>;
  const unlockedSignatures$: ValueBinding<Entity[]>;
  function removeUnlockedSignature(): void;
  const radioEnabled$: ValueBinding<boolean>;
  const volume$: ValueBinding<number>;
  const paused$: ValueBinding<boolean>;
  const muted$: ValueBinding<boolean>;
  const skipAds$: ValueBinding<boolean>;
  const emergencyMode$: ValueBinding<boolean>;
  const emergencyFocusable$: ValueBinding<boolean>;
  const emergencyMessage$: ValueBinding<LocElement | null>;
  const selectedNetwork$: ValueBinding<string | null>;
  const selectedStation$: ValueBinding<string | null>;
  const networks$: ValueBinding<RadioNetwork[]>;
  const stations$: ValueBinding<RadioStation[]>;
  const currentSegment$: ValueBinding<RadioClip | null>;
  const segmentChanged$: EventBinding<unknown>;
  function setVolume(volume: number): void;
  function setPaused(paused: boolean): void;
  function togglePaused(): void;
  function setMuted(muted: boolean): void;
  function toggleMuted(): void;
  function setSkipAds(skipAds: boolean): void;
  function toggleSkipAds(): void;
  function playPrevious(): void;
  function playNext(): void;
  function focusEmergency(): void;
  function selectNetwork(name: string): void;
  function selectStation(name: string): void;
  export interface RadioNetwork {
  	name: string;
  	nameId: string;
  	description: string;
  	descriptionId: string;
  	icon: string;
  }
  export interface RadioStation {
  	name: string;
  	nameId: string;
  	description: string;
  	icon: string;
  	network: string;
  	currentProgram: RadioProgram | null;
  	schedule: RadioProgram[];
  }
  export interface RadioProgram {
  	name: string;
  	description: string;
  	startTime: number;
  	endTime: number;
  	duration: number;
  	active: boolean;
  }
  export interface RadioClip {
  	title: string;
  	info: string | null;
  }
  const selectedEntity$: ValueBinding<Entity>;
  const selectedTrailerController$: ValueBinding<Entity>;
  const selectedUITag$: ValueBinding<string>;
  const activeSelection$: ValueBinding<boolean>;
  const selectedInfoPosition$: ValueBinding<Number2>;
  const topSections$: ValueBinding<((ResourceSection & Typed<SectionType.Resource>) | (LocalServicesSection & Typed<SectionType.LocalServices>) | (ActionsSection & Typed<SectionType.Actions>) | (DescriptionSection & Typed<SectionType.Description>) | (ContentPrerequisiteSection & Typed<SectionType.ContentPrerequisite>) | (PdxModsSection & Typed<SectionType.PdxMods>) | (DeveloperSection & Typed<SectionType.Developer>) | (ResidentsSection & Typed<SectionType.Residents>) | (HouseholdSidebarSection & Typed<SectionType.HouseholdSidebar>) | (DistrictsSection & Typed<SectionType.Districts>) | (TitleSection & Typed<SectionType.Title>) | (NotificationsSection & Typed<SectionType.Notifications>) | (PoliciesSection & Typed<SectionType.Policies>) | (ProfitabilitySection & Typed<SectionType.Profitability>) | (AverageHappinessSection & Typed<SectionType.AverageHappiness>) | (ScheduleSection & Typed<SectionType.Schedule>) | (LineSection & Typed<SectionType.Line>) | (LinesSection & Typed<SectionType.Lines>) | (ColorSection & Typed<SectionType.Color>) | (LineVisualizerSection & Typed<SectionType.LineVisualizer>) | (TicketPriceSection & Typed<SectionType.TicketPrice>) | (VehicleCountSection & Typed<SectionType.VehicleCount>) | (AttractivenessSection & Typed<SectionType.Attractiveness>) | (EfficiencySection & Typed<SectionType.Efficiency>) | (EmployeesSection & Typed<SectionType.Employees>) | (UpkeepSection & Typed<SectionType.Upkeep>) | (LevelSection & Typed<SectionType.Level>) | (EducationSection & Typed<SectionType.Education>) | (PollutionSection & Typed<SectionType.Pollution>) | (HealthcareSection & Typed<SectionType.Healthcare>) | (DeathcareSection & Typed<SectionType.Deathcare>) | (GarbageSection & Typed<SectionType.Garbage>) | (PoliceSection & Typed<SectionType.Police>) | (VehiclesSection & Typed<SectionType.Vehicles>) | (DispatchedVehiclesSection & Typed<SectionType.DispatchedVehicles>) | (ElectricitySection & Typed<SectionType.Electricity>) | (TransformerSection & Typed<SectionType.Transformer>) | (BatterySection & Typed<SectionType.Battery>) | (WaterSection & Typed<SectionType.Water>) | (SewageSection & Typed<SectionType.Sewage>) | (FireSection & Typed<SectionType.Fire>) | (PrisonSection & Typed<SectionType.Prison>) | (ShelterSection & Typed<SectionType.Shelter>) | (ParkingSection & Typed<SectionType.Parking>) | (ParkSection & Typed<SectionType.Park>) | (MailSection & Typed<SectionType.Mail>) | (RoadSection & Typed<SectionType.Road>) | (CompanySection & Typed<SectionType.Company>) | (StorageSection & Typed<SectionType.Storage>) | (TradedResourcesSection & Typed<SectionType.TradedResourcesSection>) | (PrivateVehicleSection & Typed<SectionType.PrivateVehicle>) | (PublicTransportVehicleSection & Typed<SectionType.PublicTransportVehicle>) | (CargoTransportVehicleSection & Typed<SectionType.CargoTransportVehicle>) | (DeliveryVehicleSection & Typed<SectionType.DeliveryVehicle>) | (HealthcareVehicleSection & Typed<SectionType.HealthcareVehicle>) | (FireVehicleSection & Typed<SectionType.FireVehicle>) | (PoliceVehicleSection & Typed<SectionType.PoliceVehicle>) | (MaintenanceVehicleSection & Typed<SectionType.MaintenanceVehicle>) | (DeathcareVehicleSection & Typed<SectionType.DeathcareVehicle>) | (PostVehicleSection & Typed<SectionType.PostVehicle>) | (GarbageVehicleSection & Typed<SectionType.GarbageVehicle>) | (ExtractorVehicleSection & Typed<SectionType.ExtractorVehicle>) | (PassengersSection & Typed<SectionType.Passengers>) | (CargoSection & Typed<SectionType.Cargo>) | (StatusSection & Typed<SectionType.Status>) | (CitizenSection & Typed<SectionType.Citizen>) | (SelectVehiclesSection & Typed<SectionType.SelectVehicles>) | (DestroyedBuildingSection & Typed<SectionType.DestroyedBuilding>) | (DestroyedTreeSection & Typed<SectionType.DestroyedTree>) | (ComfortSection & Typed<SectionType.Comfort>) | (UpgradesSection & Typed<SectionType.Upgrades>) | (UpgradePropertiesSection & Typed<SectionType.UpgradeProperties>) | (VisualCustomizeSection & Typed<SectionType.VisualCustomize>) | null)[]>;
  const middleSections$: ValueBinding<((ResourceSection & Typed<SectionType.Resource>) | (LocalServicesSection & Typed<SectionType.LocalServices>) | (ActionsSection & Typed<SectionType.Actions>) | (DescriptionSection & Typed<SectionType.Description>) | (ContentPrerequisiteSection & Typed<SectionType.ContentPrerequisite>) | (PdxModsSection & Typed<SectionType.PdxMods>) | (DeveloperSection & Typed<SectionType.Developer>) | (ResidentsSection & Typed<SectionType.Residents>) | (HouseholdSidebarSection & Typed<SectionType.HouseholdSidebar>) | (DistrictsSection & Typed<SectionType.Districts>) | (TitleSection & Typed<SectionType.Title>) | (NotificationsSection & Typed<SectionType.Notifications>) | (PoliciesSection & Typed<SectionType.Policies>) | (ProfitabilitySection & Typed<SectionType.Profitability>) | (AverageHappinessSection & Typed<SectionType.AverageHappiness>) | (ScheduleSection & Typed<SectionType.Schedule>) | (LineSection & Typed<SectionType.Line>) | (LinesSection & Typed<SectionType.Lines>) | (ColorSection & Typed<SectionType.Color>) | (LineVisualizerSection & Typed<SectionType.LineVisualizer>) | (TicketPriceSection & Typed<SectionType.TicketPrice>) | (VehicleCountSection & Typed<SectionType.VehicleCount>) | (AttractivenessSection & Typed<SectionType.Attractiveness>) | (EfficiencySection & Typed<SectionType.Efficiency>) | (EmployeesSection & Typed<SectionType.Employees>) | (UpkeepSection & Typed<SectionType.Upkeep>) | (LevelSection & Typed<SectionType.Level>) | (EducationSection & Typed<SectionType.Education>) | (PollutionSection & Typed<SectionType.Pollution>) | (HealthcareSection & Typed<SectionType.Healthcare>) | (DeathcareSection & Typed<SectionType.Deathcare>) | (GarbageSection & Typed<SectionType.Garbage>) | (PoliceSection & Typed<SectionType.Police>) | (VehiclesSection & Typed<SectionType.Vehicles>) | (DispatchedVehiclesSection & Typed<SectionType.DispatchedVehicles>) | (ElectricitySection & Typed<SectionType.Electricity>) | (TransformerSection & Typed<SectionType.Transformer>) | (BatterySection & Typed<SectionType.Battery>) | (WaterSection & Typed<SectionType.Water>) | (SewageSection & Typed<SectionType.Sewage>) | (FireSection & Typed<SectionType.Fire>) | (PrisonSection & Typed<SectionType.Prison>) | (ShelterSection & Typed<SectionType.Shelter>) | (ParkingSection & Typed<SectionType.Parking>) | (ParkSection & Typed<SectionType.Park>) | (MailSection & Typed<SectionType.Mail>) | (RoadSection & Typed<SectionType.Road>) | (CompanySection & Typed<SectionType.Company>) | (StorageSection & Typed<SectionType.Storage>) | (TradedResourcesSection & Typed<SectionType.TradedResourcesSection>) | (PrivateVehicleSection & Typed<SectionType.PrivateVehicle>) | (PublicTransportVehicleSection & Typed<SectionType.PublicTransportVehicle>) | (CargoTransportVehicleSection & Typed<SectionType.CargoTransportVehicle>) | (DeliveryVehicleSection & Typed<SectionType.DeliveryVehicle>) | (HealthcareVehicleSection & Typed<SectionType.HealthcareVehicle>) | (FireVehicleSection & Typed<SectionType.FireVehicle>) | (PoliceVehicleSection & Typed<SectionType.PoliceVehicle>) | (MaintenanceVehicleSection & Typed<SectionType.MaintenanceVehicle>) | (DeathcareVehicleSection & Typed<SectionType.DeathcareVehicle>) | (PostVehicleSection & Typed<SectionType.PostVehicle>) | (GarbageVehicleSection & Typed<SectionType.GarbageVehicle>) | (ExtractorVehicleSection & Typed<SectionType.ExtractorVehicle>) | (PassengersSection & Typed<SectionType.Passengers>) | (CargoSection & Typed<SectionType.Cargo>) | (StatusSection & Typed<SectionType.Status>) | (CitizenSection & Typed<SectionType.Citizen>) | (SelectVehiclesSection & Typed<SectionType.SelectVehicles>) | (DestroyedBuildingSection & Typed<SectionType.DestroyedBuilding>) | (DestroyedTreeSection & Typed<SectionType.DestroyedTree>) | (ComfortSection & Typed<SectionType.Comfort>) | (UpgradesSection & Typed<SectionType.Upgrades>) | (UpgradePropertiesSection & Typed<SectionType.UpgradeProperties>) | (VisualCustomizeSection & Typed<SectionType.VisualCustomize>) | null)[]>;
  const bottomSections$: ValueBinding<((ResourceSection & Typed<SectionType.Resource>) | (LocalServicesSection & Typed<SectionType.LocalServices>) | (ActionsSection & Typed<SectionType.Actions>) | (DescriptionSection & Typed<SectionType.Description>) | (ContentPrerequisiteSection & Typed<SectionType.ContentPrerequisite>) | (PdxModsSection & Typed<SectionType.PdxMods>) | (DeveloperSection & Typed<SectionType.Developer>) | (ResidentsSection & Typed<SectionType.Residents>) | (HouseholdSidebarSection & Typed<SectionType.HouseholdSidebar>) | (DistrictsSection & Typed<SectionType.Districts>) | (TitleSection & Typed<SectionType.Title>) | (NotificationsSection & Typed<SectionType.Notifications>) | (PoliciesSection & Typed<SectionType.Policies>) | (ProfitabilitySection & Typed<SectionType.Profitability>) | (AverageHappinessSection & Typed<SectionType.AverageHappiness>) | (ScheduleSection & Typed<SectionType.Schedule>) | (LineSection & Typed<SectionType.Line>) | (LinesSection & Typed<SectionType.Lines>) | (ColorSection & Typed<SectionType.Color>) | (LineVisualizerSection & Typed<SectionType.LineVisualizer>) | (TicketPriceSection & Typed<SectionType.TicketPrice>) | (VehicleCountSection & Typed<SectionType.VehicleCount>) | (AttractivenessSection & Typed<SectionType.Attractiveness>) | (EfficiencySection & Typed<SectionType.Efficiency>) | (EmployeesSection & Typed<SectionType.Employees>) | (UpkeepSection & Typed<SectionType.Upkeep>) | (LevelSection & Typed<SectionType.Level>) | (EducationSection & Typed<SectionType.Education>) | (PollutionSection & Typed<SectionType.Pollution>) | (HealthcareSection & Typed<SectionType.Healthcare>) | (DeathcareSection & Typed<SectionType.Deathcare>) | (GarbageSection & Typed<SectionType.Garbage>) | (PoliceSection & Typed<SectionType.Police>) | (VehiclesSection & Typed<SectionType.Vehicles>) | (DispatchedVehiclesSection & Typed<SectionType.DispatchedVehicles>) | (ElectricitySection & Typed<SectionType.Electricity>) | (TransformerSection & Typed<SectionType.Transformer>) | (BatterySection & Typed<SectionType.Battery>) | (WaterSection & Typed<SectionType.Water>) | (SewageSection & Typed<SectionType.Sewage>) | (FireSection & Typed<SectionType.Fire>) | (PrisonSection & Typed<SectionType.Prison>) | (ShelterSection & Typed<SectionType.Shelter>) | (ParkingSection & Typed<SectionType.Parking>) | (ParkSection & Typed<SectionType.Park>) | (MailSection & Typed<SectionType.Mail>) | (RoadSection & Typed<SectionType.Road>) | (CompanySection & Typed<SectionType.Company>) | (StorageSection & Typed<SectionType.Storage>) | (TradedResourcesSection & Typed<SectionType.TradedResourcesSection>) | (PrivateVehicleSection & Typed<SectionType.PrivateVehicle>) | (PublicTransportVehicleSection & Typed<SectionType.PublicTransportVehicle>) | (CargoTransportVehicleSection & Typed<SectionType.CargoTransportVehicle>) | (DeliveryVehicleSection & Typed<SectionType.DeliveryVehicle>) | (HealthcareVehicleSection & Typed<SectionType.HealthcareVehicle>) | (FireVehicleSection & Typed<SectionType.FireVehicle>) | (PoliceVehicleSection & Typed<SectionType.PoliceVehicle>) | (MaintenanceVehicleSection & Typed<SectionType.MaintenanceVehicle>) | (DeathcareVehicleSection & Typed<SectionType.DeathcareVehicle>) | (PostVehicleSection & Typed<SectionType.PostVehicle>) | (GarbageVehicleSection & Typed<SectionType.GarbageVehicle>) | (ExtractorVehicleSection & Typed<SectionType.ExtractorVehicle>) | (PassengersSection & Typed<SectionType.Passengers>) | (CargoSection & Typed<SectionType.Cargo>) | (StatusSection & Typed<SectionType.Status>) | (CitizenSection & Typed<SectionType.Citizen>) | (SelectVehiclesSection & Typed<SectionType.SelectVehicles>) | (DestroyedBuildingSection & Typed<SectionType.DestroyedBuilding>) | (DestroyedTreeSection & Typed<SectionType.DestroyedTree>) | (ComfortSection & Typed<SectionType.Comfort>) | (UpgradesSection & Typed<SectionType.Upgrades>) | (UpgradePropertiesSection & Typed<SectionType.UpgradeProperties>) | (VisualCustomizeSection & Typed<SectionType.VisualCustomize>) | null)[]>;
  const titleSection$: ValueBinding<TitleSection | null>;
  const developerSection$: ValueBinding<DeveloperSection | null>;
  const lineVisualizerSection$: ValueBinding<LineVisualizerSection | null>;
  const householdSidebarSection$: ValueBinding<HouseholdSidebarSection | null>;
  const tooltipTags$: ValueBinding<string[]>;
  const selectedRoute$: ValueBinding<Entity>;
  const selectEntity: (entity: Entity) => void;
  const setSelectedRoute: (entity: Entity) => void;
  const clearSelection: () => void;
  export enum SectionType {
  	Resource = "Game.UI.InGame.ResourceSection",
  	LocalServices = "Game.UI.InGame.LocalServicesSection",
  	Actions = "Game.UI.InGame.ActionsSection",
  	Description = "Game.UI.InGame.DescriptionSection",
  	ContentPrerequisite = "Game.UI.InGame.ContentPrerequisiteSection",
  	PdxMods = "Game.UI.InGame.PdxModsSection",
  	Developer = "Game.UI.InGame.DeveloperSection",
  	Residents = "Game.UI.InGame.ResidentsSection",
  	HouseholdSidebar = "Game.UI.InGame.HouseholdSidebarSection",
  	Districts = "Game.UI.InGame.DistrictsSection",
  	Title = "Game.UI.InGame.TitleSection",
  	Notifications = "Game.UI.InGame.NotificationsSection",
  	Policies = "Game.UI.InGame.PoliciesSection",
  	Profitability = "Game.UI.InGame.ProfitabilitySection",
  	AverageHappiness = "Game.UI.InGame.AverageHappinessSection",
  	Schedule = "Game.UI.InGame.ScheduleSection",
  	Line = "Game.UI.InGame.LineSection",
  	Lines = "Game.UI.InGame.LinesSection",
  	Color = "Game.UI.InGame.ColorSection",
  	LineVisualizer = "Game.UI.InGame.LineVisualizerSection",
  	TicketPrice = "Game.UI.InGame.TicketPriceSection",
  	VehicleCount = "Game.UI.InGame.VehicleCountSection",
  	Attractiveness = "Game.UI.InGame.AttractivenessSection",
  	Efficiency = "Game.UI.InGame.EfficiencySection",
  	Employees = "Game.UI.InGame.EmployeesSection",
  	Upkeep = "Game.UI.InGame.UpkeepSection",
  	Level = "Game.UI.InGame.LevelSection",
  	Education = "Game.UI.InGame.EducationSection",
  	Pollution = "Game.UI.InGame.PollutionSection",
  	Healthcare = "Game.UI.InGame.HealthcareSection",
  	Deathcare = "Game.UI.InGame.DeathcareSection",
  	Garbage = "Game.UI.InGame.GarbageSection",
  	Police = "Game.UI.InGame.PoliceSection",
  	Vehicles = "Game.UI.InGame.VehiclesSection",
  	DispatchedVehicles = "Game.UI.InGame.DispatchedVehiclesSection",
  	Electricity = "Game.UI.InGame.ElectricitySection",
  	Transformer = "Game.UI.InGame.TransformerSection",
  	Battery = "Game.UI.InGame.BatterySection",
  	Water = "Game.UI.InGame.WaterSection",
  	Sewage = "Game.UI.InGame.SewageSection",
  	Fire = "Game.UI.InGame.FireSection",
  	Prison = "Game.UI.InGame.PrisonSection",
  	Shelter = "Game.UI.InGame.ShelterSection",
  	Parking = "Game.UI.InGame.ParkingSection",
  	Park = "Game.UI.InGame.ParkSection",
  	Mail = "Game.UI.InGame.MailSection",
  	Road = "Game.UI.InGame.RoadSection",
  	Company = "Game.UI.InGame.CompanySection",
  	Storage = "Game.UI.InGame.StorageSection",
  	TradedResourcesSection = "Game.UI.InGame.TradedResourcesSection",
  	PrivateVehicle = "Game.UI.InGame.PrivateVehicleSection",
  	PublicTransportVehicle = "Game.UI.InGame.PublicTransportVehicleSection",
  	CargoTransportVehicle = "Game.UI.InGame.CargoTransportVehicleSection",
  	DeliveryVehicle = "Game.UI.InGame.DeliveryVehicleSection",
  	HealthcareVehicle = "Game.UI.InGame.HealthcareVehicleSection",
  	FireVehicle = "Game.UI.InGame.FireVehicleSection",
  	PoliceVehicle = "Game.UI.InGame.PoliceVehicleSection",
  	MaintenanceVehicle = "Game.UI.InGame.MaintenanceVehicleSection",
  	DeathcareVehicle = "Game.UI.InGame.DeathcareVehicleSection",
  	PostVehicle = "Game.UI.InGame.PostVehicleSection",
  	GarbageVehicle = "Game.UI.InGame.GarbageVehicleSection",
  	ExtractorVehicle = "Game.UI.InGame.ExtractorVehicleSection",
  	Passengers = "Game.UI.InGame.PassengersSection",
  	Cargo = "Game.UI.InGame.CargoSection",
  	Load = "Game.UI.InGame.LoadSection",
  	Status = "Game.UI.InGame.StatusSection",
  	Citizen = "Game.UI.InGame.CitizenSection",
  	DummyHuman = "Game.UI.InGame.DummyHumanSection",
  	Animal = "Game.UI.InGame.AnimalSection",
  	SelectVehicles = "Game.UI.InGame.SelectVehiclesSection",
  	DestroyedBuilding = "Game.UI.InGame.DestroyedBuildingSection",
  	DestroyedTree = "Game.UI.InGame.DestroyedTreeSection",
  	Comfort = "Game.UI.InGame.ComfortSection",
  	Upgrades = "Game.UI.InGame.UpgradesSection",
  	UpgradeProperties = "Game.UI.InGame.UpgradePropertiesSection",
  	VisualCustomize = "Game.UI.InGame.VisualCustomizeSection"
  }
  export interface SelectedInfoSections {
  	[SectionType.Resource]: ResourceSection;
  	[SectionType.LocalServices]: LocalServicesSection;
  	[SectionType.Actions]: ActionsSection;
  	[SectionType.Description]: DescriptionSection;
  	[SectionType.ContentPrerequisite]: ContentPrerequisiteSection;
  	[SectionType.PdxMods]: PdxModsSection;
  	[SectionType.Developer]: DeveloperSection;
  	[SectionType.Residents]: ResidentsSection;
  	[SectionType.HouseholdSidebar]: HouseholdSidebarSection;
  	[SectionType.Districts]: DistrictsSection;
  	[SectionType.Title]: TitleSection;
  	[SectionType.Notifications]: NotificationsSection;
  	[SectionType.Policies]: PoliciesSection;
  	[SectionType.Profitability]: ProfitabilitySection;
  	[SectionType.AverageHappiness]: AverageHappinessSection;
  	[SectionType.Schedule]: ScheduleSection;
  	[SectionType.Line]: LineSection;
  	[SectionType.Lines]: LinesSection;
  	[SectionType.Color]: ColorSection;
  	[SectionType.LineVisualizer]: LineVisualizerSection;
  	[SectionType.TicketPrice]: TicketPriceSection;
  	[SectionType.VehicleCount]: VehicleCountSection;
  	[SectionType.Attractiveness]: AttractivenessSection;
  	[SectionType.Efficiency]: EfficiencySection;
  	[SectionType.Employees]: EmployeesSection;
  	[SectionType.Upkeep]: UpkeepSection;
  	[SectionType.Level]: LevelSection;
  	[SectionType.Education]: EducationSection;
  	[SectionType.Pollution]: PollutionSection;
  	[SectionType.Healthcare]: HealthcareSection;
  	[SectionType.Deathcare]: DeathcareSection;
  	[SectionType.Garbage]: GarbageSection;
  	[SectionType.Police]: PoliceSection;
  	[SectionType.Vehicles]: VehiclesSection;
  	[SectionType.DispatchedVehicles]: DispatchedVehiclesSection;
  	[SectionType.Electricity]: ElectricitySection;
  	[SectionType.Transformer]: TransformerSection;
  	[SectionType.Battery]: BatterySection;
  	[SectionType.Water]: WaterSection;
  	[SectionType.Sewage]: SewageSection;
  	[SectionType.Fire]: FireSection;
  	[SectionType.Prison]: PrisonSection;
  	[SectionType.Shelter]: ShelterSection;
  	[SectionType.Parking]: ParkingSection;
  	[SectionType.Park]: ParkSection;
  	[SectionType.Mail]: MailSection;
  	[SectionType.Road]: RoadSection;
  	[SectionType.Company]: CompanySection;
  	[SectionType.Storage]: StorageSection;
  	[SectionType.TradedResourcesSection]: TradedResourcesSection;
  	[SectionType.PrivateVehicle]: PrivateVehicleSection;
  	[SectionType.PublicTransportVehicle]: PublicTransportVehicleSection;
  	[SectionType.CargoTransportVehicle]: CargoTransportVehicleSection;
  	[SectionType.DeliveryVehicle]: DeliveryVehicleSection;
  	[SectionType.HealthcareVehicle]: HealthcareVehicleSection;
  	[SectionType.FireVehicle]: FireVehicleSection;
  	[SectionType.PoliceVehicle]: PoliceVehicleSection;
  	[SectionType.MaintenanceVehicle]: MaintenanceVehicleSection;
  	[SectionType.DeathcareVehicle]: DeathcareVehicleSection;
  	[SectionType.PostVehicle]: PostVehicleSection;
  	[SectionType.GarbageVehicle]: GarbageVehicleSection;
  	[SectionType.ExtractorVehicle]: ExtractorVehicleSection;
  	[SectionType.Passengers]: PassengersSection;
  	[SectionType.Cargo]: CargoSection;
  	[SectionType.Load]: LoadSection;
  	[SectionType.Status]: StatusSection;
  	[SectionType.Citizen]: CitizenSection;
  	[SectionType.DummyHuman]: DummyHumanSection;
  	[SectionType.Animal]: AnimalSection;
  	[SectionType.SelectVehicles]: SelectVehiclesSection;
  	[SectionType.DestroyedBuilding]: DestroyedBuildingSection;
  	[SectionType.DestroyedTree]: DestroyedTreeSection;
  	[SectionType.Comfort]: ComfortSection;
  	[SectionType.Upgrades]: UpgradesSection;
  	[SectionType.UpgradeProperties]: UpgradePropertiesSection;
  	[SectionType.VisualCustomize]: VisualCustomizeSection;
  }
  export enum SelectedInfoPanelTab {
  	Default = 0,
  	Customize = 1
  }
  export type SelectedInfoSection = TypeFromMap<SelectedInfoSections>;
  export interface SelectedInfoSectionBase {
  	group: string;
  	tooltipKeys: string[];
  	tooltipTags: string[];
  }
  export interface ResourceSection extends SelectedInfoSectionBase {
  	resourceAmount: number;
  	resourceKey: string;
  }
  export interface LocalServicesSection extends SelectedInfoSectionBase {
  	localServiceBuildings: LocalServiceBuilding[];
  }
  export interface ActionsSection extends SelectedInfoSectionBase {
  	focusable: boolean;
  	focusing: boolean;
  	following: boolean;
  	followable: boolean;
  	moveable: boolean;
  	deletable: boolean;
  	disabled: boolean;
  	disableable: boolean;
  	emptying: boolean;
  	emptiable: boolean;
  	hasLotTool: boolean;
  	hasTrafficRoutes: boolean;
  }
  export interface DescriptionSection extends SelectedInfoSectionBase {
  	localeId: string;
  	effects: PrefabEffect[];
  }
  export interface ContentPrerequisiteSection extends SelectedInfoSectionBase {
  	contentPrefab: string;
  	modContentPrefab: string;
  }
  export interface PdxModsSection extends SelectedInfoSectionBase {
  	modId: string | null;
  }
  export interface DeveloperSection extends SelectedInfoSectionBase {
  	subsections: DeveloperSubsection[];
  }
  export interface ResidentsSection extends SelectedInfoSectionBase {
  	isHousehold: boolean;
  	isDistrict: boolean;
  	householdCount: number;
  	maxHouseholds: number;
  	residentCount: number;
  	petCount: number;
  	wealthKey: string;
  	wealthData: HouseholdWealthData;
  	residence: Name;
  	residenceEntity: Entity;
  	residenceKey: string;
  	educationData: ChartData;
  	ageData: ChartData;
  }
  export interface HouseholdWealthData {
  	income: number;
  	rent: number;
  	upkeep: number;
  	resourceCost: number;
  	fees: number;
  }
  export interface HouseholdSidebarSection extends SelectedInfoSectionBase {
  	variant: HouseholdSidebarVariant;
  	residence: HouseholdSidebarItem;
  	household: HouseholdSidebarItem;
  	households: number;
  	residents: number;
  	pets: number;
  }
  export interface DistrictsSection extends SelectedInfoSectionBase {
  	districtMissing: boolean;
  	districts: District[];
  }
  export interface TitleSection extends SelectedInfoSectionBase {
  	name: Name | null;
  	vkName: Name | null;
  	vkLocaleKey: string;
  	icon: string | null;
  }
  export interface NotificationsSection extends SelectedInfoSectionBase {
  	notifications: NotificationData[];
  }
  export interface PoliciesSection extends SelectedInfoSectionBase {
  	policies: PolicyData[];
  }
  export interface ProfitabilitySection extends SelectedInfoSectionBase {
  	profitability: NotificationData;
  	profitabilityFactors: Factor[];
  }
  export interface AverageHappinessSection extends SelectedInfoSectionBase {
  	averageHappiness: NotificationData;
  	happinessFactors: Factor[];
  }
  export interface VisualCustomizeSection extends SelectedInfoSectionBase {
  	meshColor: ColorSet;
  	colorSets: ColorSet[];
  	hasMeshColors: boolean;
  	canBeHistorical: boolean;
  	isHistorical: boolean;
  }
  export interface ScheduleSection extends SelectedInfoSectionBase {
  	schedule: number;
  }
  export interface LineSection extends SelectedInfoSectionBase {
  	length: number;
  	stops: number;
  	usage: number;
  	cargo: number;
  }
  export interface LinesSection extends SelectedInfoSectionBase {
  	hasLines: boolean;
  	lines: Line[];
  	hasPassengers: boolean;
  	passengers: number;
  }
  export interface ColorSection extends SelectedInfoSectionBase {
  	color: Color;
  }
  export interface LineVisualizerSection extends SelectedInfoSectionBase {
  	color: Color;
  	stops: LineStop[];
  	vehicles: LineVehicle[];
  	segments: LineSegment[];
  	stopCapacity: number;
  }
  export interface TicketPriceSection extends SelectedInfoSectionBase {
  	sliderData: PolicySliderData;
  }
  export interface VehicleCountSection extends SelectedInfoSectionBase {
  	vehicleCount: number;
  	activeVehicles: number;
  	vehicleCountMin: number;
  	vehicleCountMax: number;
  }
  export interface SelectVehiclesSection extends SelectedInfoSectionBase {
  	routePrefab: string;
  	selectedPrimaryVehicles: VehiclePrefab[];
  	selectedSecondaryVehicles: VehiclePrefab[];
  	availablePrimaryVehicles: VehiclePrefab[];
  	availableSecondaryVehicles: VehiclePrefab[];
  }
  export interface AttractivenessSection extends SelectedInfoSectionBase {
  	attractiveness: number;
  	baseAttractiveness: number;
  	factors: AttractivenessFactor[];
  }
  export interface EfficiencySection extends SelectedInfoSectionBase {
  	efficiency: number;
  	factors: EfficiencyFactor[];
  }
  export interface EmployeesSection extends SelectedInfoSectionBase {
  	employeeCount: number;
  	maxEmployees: number;
  	educationDataEmployees: ChartData;
  	educationDataWorkplaces: ChartData;
  }
  export interface UpkeepSection extends SelectedInfoSectionBase {
  	upkeeps: UpkeepItem[];
  	wages: number;
  	total: number;
  	inactive: boolean;
  }
  export interface LevelSection extends SelectedInfoSectionBase {
  	level: number;
  	maxLevel: number;
  	isUnderConstruction: boolean;
  	progress: number;
  	zone: string;
  }
  export interface EducationSection extends SelectedInfoSectionBase {
  	studentCount: number;
  	studentCapacity: number;
  	graduationTime: number;
  	failProbability: number;
  }
  export interface PollutionSection extends SelectedInfoSectionBase {
  	groundPollutionKey: Pollution$1;
  	airPollutionKey: Pollution$1;
  	noisePollutionKey: Pollution$1;
  }
  export enum Pollution$1 {
  	none = 0,
  	low = 1,
  	medium = 2,
  	high = 3
  }
  export interface HealthcareSection extends SelectedInfoSectionBase {
  	patientCount: number;
  	patientCapacity: number;
  }
  export interface DeathcareSection extends SelectedInfoSectionBase {
  	bodyCount: number;
  	bodyCapacity: number;
  	processingSpeed: number;
  	processingCapacity: number;
  }
  export interface GarbageSection extends SelectedInfoSectionBase {
  	garbage: number;
  	garbageCapacity: number;
  	processingSpeed: number;
  	processingCapacity: number;
  	loadKey: string;
  }
  export interface PoliceSection extends SelectedInfoSectionBase {
  	prisonerCount: number;
  	prisonerCapacity: number;
  }
  export interface VehiclesSection extends SelectedInfoSectionBase {
  	vehicleKey: string;
  	vehicleCount: number;
  	availableVehicleCount: number;
  	vehicleCapacity: number;
  	vehicleList: Vehicle[];
  }
  export interface DispatchedVehiclesSection extends SelectedInfoSectionBase {
  	vehicleList: Vehicle[];
  }
  export interface ElectricitySection extends SelectedInfoSectionBase {
  	capacity: number;
  	production: number;
  }
  export interface TransformerSection extends SelectedInfoSectionBase {
  	capacity: number;
  	flow: number;
  }
  export interface BatterySection extends SelectedInfoSectionBase {
  	batteryCharge: number;
  	batteryCapacity: number;
  	flow: number;
  	remainingTime: number;
  }
  export interface WaterSection extends SelectedInfoSectionBase {
  	pollution: number;
  	capacity: number;
  	lastProduction: number;
  }
  export interface SewageSection extends SelectedInfoSectionBase {
  	capacity: number;
  	lastProcessed: number;
  	lastPurified: number;
  	purification: number;
  }
  export interface FireSection extends SelectedInfoSectionBase {
  	vehicleEfficiency: number;
  	disasterResponder: boolean;
  }
  export interface PrisonSection extends SelectedInfoSectionBase {
  	prisonerCount: number;
  	prisonerCapacity: number;
  }
  export interface ShelterSection extends SelectedInfoSectionBase {
  	sheltered: number;
  	shelterCapacity: number;
  }
  export interface ParkingSection extends SelectedInfoSectionBase {
  	parkedCars: number;
  	parkingCapacity: number;
  }
  export interface ParkSection extends SelectedInfoSectionBase {
  	maintenance: number;
  }
  export interface MailSection extends SelectedInfoSectionBase {
  	sortingRate: number;
  	sortingCapacity: number;
  	localAmount: number;
  	unsortedAmount: number;
  	outgoingAmount: number;
  	storedAmount: number;
  	storageCapacity: number;
  	localKey: string;
  	unsortedKey: string;
  	type: MailSectionType;
  }
  export enum MailSectionType {
  	PostFacility = 0,
  	MailBox = 1
  }
  export interface RoadSection extends SelectedInfoSectionBase {
  	volumeData?: number[];
  	flowData?: number[];
  	length: number;
  	bestCondition?: number;
  	worstCondition?: number;
  	condition?: number;
  	upkeep: number;
  }
  export interface CompanySection extends SelectedInfoSectionBase {
  	companyName: Name | null;
  	isRentable: boolean;
  	input1: string | null;
  	input2: string | null;
  	output: string | null;
  	outputUnit: Unit | null;
  	sells: string | null;
  	stores: string | null;
  	hotelGuests: Number2 | null;
  	isCommercial?: boolean;
  	isStorage?: boolean;
  	BankBalance: number;
  	DailyCustomers: number;
  	ElectricityPaid: number;
  	GarbageFeePaid: number;
  	Income: number;
  	MaxNumberOfCustomers: number;
  	Profit: number;
  	RentPaid: number;
  	ResourcesBoughtPaid: number;
  	SewageFeePaid: number;
  	TaxesPaid: number;
  	WagesPaid: number;
  	WaterFeePaid: number;
  	ProductionRate: number;
  	industrialType: string | null;
  }
  export interface StorageSection extends SelectedInfoSectionBase {
  	stored: number;
  	capacity: number;
  	status: string;
  	storageType: string;
  	rawMaterials: Resource$1[];
  	processedGoods: Resource$1[];
  	mail: Resource$1[];
  }
  export interface TradedResourcesSection extends SelectedInfoSectionBase {
  	rawMaterials: Resource$1[];
  	processedGoods: Resource$1[];
  	mail: Resource$1[];
  }
  export interface DestroyedBuildingSection extends SelectedInfoSectionBase {
  	destroyer: string | null;
  	cleared: boolean;
  	progress: number;
  	status: string;
  }
  export interface DestroyedTreeSection extends SelectedInfoSectionBase {
  	destroyer: string | null;
  }
  export interface ComfortSection extends SelectedInfoSectionBase {
  	comfort: number;
  }
  export interface UpgradesSection extends SelectedInfoSectionBase {
  	extensions: Upgrade[];
  	subBuildings: Upgrade[];
  }
  export interface UpgradePropertiesSection extends SelectedInfoSectionBase {
  	mainBuilding: Entity;
  	mainBuildingName: Name;
  	upgrade: Entity;
  	type: UpgradeType;
  }
  export interface VehicleSection extends SelectedInfoSectionBase {
  	stateKey: string;
  	owner: Location$1;
  	fromOutside: boolean;
  	nextStop: Location$1;
  }
  export interface VehicleSectionProps extends SelectedInfoSectionProps {
  	stateKey: string;
  	owner: Location$1;
  	fromOutside: boolean;
  	nextStop: Location$1;
  }
  export interface VehicleWithLineSection extends VehicleSection {
  	line: Name | null;
  	lineEntity: Entity;
  }
  export interface VehicleWithLineSectionProps extends VehicleSectionProps {
  	line: Name | null;
  	lineEntity: Entity;
  }
  export interface PrivateVehicleSection extends VehicleSection {
  	keeper: Name | null;
  	keeperEntity: Entity | null;
  	vehicleKey: string;
  }
  export interface PublicTransportVehicleSection extends VehicleWithLineSection {
  	vehicleKey: string;
  	showDestination: boolean;
  }
  export interface CargoTransportVehicleSection extends VehicleWithLineSection {
  }
  export interface DeliveryVehicleSection extends VehicleSection {
  	resourceKey: string;
  	vehicleKey: string;
  }
  export interface HealthcareVehicleSection extends VehicleSection {
  	patient: Name | null;
  	patientEntity: Entity | null;
  	vehicleKey: string;
  }
  export interface FireVehicleSection extends VehicleSection {
  	vehicleKey: string;
  }
  export interface PoliceVehicleSection extends VehicleSection {
  	criminal: Name | null;
  	criminalEntity: Entity | null;
  	vehicleKey: string;
  }
  export interface MaintenanceVehicleSection extends VehicleSection {
  	workShift: number;
  }
  export interface DeathcareVehicleSection extends VehicleSection {
  	dead: Name | null;
  	deadEntity: Entity | null;
  }
  export interface PostVehicleSection extends VehicleSection {
  }
  export interface GarbageVehicleSection extends VehicleSection {
  	vehicleKey: string;
  }
  export interface ExtractorVehicleSection extends VehicleSection {
  	vehicleKey: string;
  }
  export interface PassengersSection extends SelectedInfoSectionBase {
  	expanded: boolean;
  	passengers: number;
  	maxPassengers: number;
  	pets: number;
  	vehiclePassengerKey: string;
  }
  export interface CargoSection extends SelectedInfoSectionBase {
  	expanded: boolean;
  	cargo: number;
  	capacity: number;
  	rawMaterials: Resource$1[];
  	processedGoods: Resource$1[];
  	mail: Resource$1[];
  	cargoKey: string;
  }
  export interface LoadSection extends SelectedInfoSectionBase {
  	__Type: "Game.UI.InGame.LoadSection";
  	expanded: boolean;
  	load: number;
  	capacity: number;
  	loadKey: string;
  }
  export interface StatusSection extends SelectedInfoSectionBase {
  	conditions: NotificationData[];
  	notifications: NotificationData[];
  	happiness: NotificationData | null;
  }
  export interface CitizenSection extends SelectedInfoSectionBase {
  	citizenKey: string;
  	stateKey: string;
  	household: Name | null;
  	householdEntity: Entity | null;
  	residence: Name | null;
  	residenceEntity: Entity | null;
  	residenceKey: string;
  	workplace: Name | null;
  	workplaceEntity: Entity | null;
  	workplaceKey: string;
  	occupationKey: string;
  	jobLevelKey: string;
  	school: Name | null;
  	schoolEntity: Entity | null;
  	schoolLevel: number;
  	educationKey: string;
  	ageKey: string;
  	wealthKey: string;
  	destination: Name | null;
  	destinationEntity: Entity | null;
  	destinationKey: string;
  }
  export interface DummyHumanSection extends SelectedInfoSectionBase {
  	__Type: "Game.UI.InGame.DummyHumanSection";
  	origin: Name | null;
  	originEntity: Entity | null;
  	destination: Name | null;
  	destinationEntity: Entity | null;
  }
  export interface AnimalSection extends SelectedInfoSectionBase {
  	__Type: "Game.UI.InGame.AnimalSection";
  	typeKey: string;
  	owner: Name | null;
  	ownerEntity: Entity | null;
  	destination: Name | null;
  	destinationEntity: Entity | null;
  }
  export enum DeveloperSubsectionType {
  	GenericInfo = "Game.UI.InGame.GenericInfo",
  	CapacityInfo = "Game.UI.InGame.CapacityInfo",
  	InfoList = "Game.UI.InGame.InfoList"
  }
  export interface DeveloperSubsections {
  	[DeveloperSubsectionType.GenericInfo]: GenericInfo;
  	[DeveloperSubsectionType.CapacityInfo]: CapacityInfo;
  	[DeveloperSubsectionType.InfoList]: InfoList;
  }
  export type DeveloperSubsection = TypeFromMap<DeveloperSubsections>;
  export interface GenericInfo {
  	label: string;
  	value: string;
  	target: Entity | null;
  }
  export interface CapacityInfo {
  	label: string;
  	value: number;
  	max: number;
  }
  export interface InfoList {
  	label: string;
  	list: Item[];
  }
  export interface District {
  	name: Name;
  	entity: Entity;
  }
  export interface UpgradeInfo {
  	id: string;
  	typeIdKey: string;
  	icon: string;
  	cost: number;
  	locked: boolean;
  	forbidden: boolean;
  	entity: Entity;
  }
  export interface Upgrade {
  	name: Name;
  	entity: Entity;
  	disabled: boolean;
  	focused: boolean;
  }
  export enum UpgradeType {
  	Extension = "Extension",
  	SubBuilding = "SubBuilding"
  }
  export interface Item {
  	text: string;
  	entity: Entity | null;
  }
  interface Resource$1 {
  	key: string;
  	amount: number;
  	status: string;
  }
  export interface SelectedInfoSectionProps {
  	group: string;
  	tooltipKeys: string[];
  	tooltipTags: string[];
  	focusKey?: FocusKey;
  }
  export interface Line {
  	name: Name;
  	color: Color;
  	entity: Entity;
  	active: boolean;
  }
  export enum LineType {
  	Passenger = 0,
  	Cargo = 1,
  	Work = 2
  }
  export type LineItem = LineStop | LineVehicle;
  const LINE_STOP = "Game.UI.InGame.LineVisualizerSection+LineStop";
  export interface LineStop extends Typed<typeof LINE_STOP> {
  	entity: Entity;
  	name: Name;
  	position: number;
  	cargo: number;
  	capacity: number;
  	type: LineType;
  	isOutsideConnection: boolean;
  }
  const LINE_VEHICLE = "Game.UI.InGame.LineVisualizerSection+LineVehicle";
  export interface LineVehicle extends Typed<typeof LINE_VEHICLE> {
  	entity: Entity;
  	name: Name;
  	position: number;
  	cargo: number;
  	capacity: number;
  	type: LineType;
  }
  export interface LineSegment {
  	start: number;
  	end: number;
  	broken: boolean;
  }
  export interface Vehicle {
  	entity: Entity;
  	name: Name;
  	stateKey: string;
  	vehicleKey: string;
  }
  export interface VehiclePrefab {
  	entity: Entity;
  	id: string;
  	locked: boolean;
  	multiunit: boolean;
  	requirements: PrefabRequirement[];
  	thumbnail: string;
  	objectRequirementIcons: string[] | null;
  }
  export interface LocalServiceBuilding {
  	name: Name;
  	serviceIcon: string | null;
  	entity: Entity;
  }
  export interface EfficiencyFactor {
  	factor: string;
  	value: number;
  	result: number;
  }
  export interface AttractivenessFactor {
  	localeKey: string;
  	delta: number;
  }
  export interface UpkeepItem {
  	count: number;
  	amount: number;
  	price: number;
  	titleId: string;
  	localeKey: string;
  }
  interface Location$1 {
  	entity: Entity;
  	name: Name;
  }
  export interface HouseholdSidebarItem {
  	entity: Entity;
  	name: Name;
  	familyName: Name;
  	icon: string | null;
  	selected: boolean;
  	count: number | null;
  }
  export enum HouseholdSidebarVariant {
  	Citizen = "Citizen",
  	Household = "Household",
  	Building = "Building"
  }
  export interface ColorSet {
  	channel0: Color;
  	channel1: Color;
  	channel2: Color;
  }
  function useTooltipParagraph(tooltipIdHashKey: string): string | null;
  function useTooltipParagraphs(tooltipIdHashKeys: (string | null)[]): string[] | null;
  function useGeneratedTooltipParagraphs(group: string, tooltipKeys: string[], tooltipTags: string[], hideGroupParagraph?: boolean): string[] | null;
  export interface StatCategory {
  	entity: Entity;
  	key: string;
  	locked: boolean;
  }
  export interface StatItem {
  	category: Entity;
  	group: Entity;
  	entity: Entity;
  	statisticType: number;
  	unitType: number;
  	parameterIndex: number;
  	key: string;
  	color: Color;
  	locked: boolean;
  	isGroup: boolean;
  	isSubgroup: boolean;
  	stacked: boolean;
  }
  const sampleRange$: ValueBinding<number>;
  const sampleCount$: ValueBinding<number>;
  const activeGroup$: ValueBinding<Entity>;
  const activeCategory$: ValueBinding<Entity>;
  const stacked$: ValueBinding<boolean>;
  const statisticsCategories$: ValueBinding<StatCategory[]>;
  const statLabels$: ValueBinding<number[]>;
  const statGroupsMap$: MapBinding<Entity, StatItem[]>;
  const statsData$: ValueBinding<ChartDataset<"line", Number2[]>[]>;
  const selectedStatistics$: ValueBinding<StatItem[]>;
  const statUnlockingRequirements$: MapBinding<Entity, UnlockingRequirements>;
  const updatesPerDay$: ValueBinding<number>;
  function addStat(stat: StatItem): void;
  function addStatChildren(stat: StatItem): void;
  function removeStat(stat: StatItem): void;
  function clearStats(): void;
  function setSampleRange(range: number): void;
  const taxRate: ValueBinding<number>;
  const taxEffect: ValueBinding<number>;
  const taxIncome: ValueBinding<number>;
  const minTaxRate: ValueBinding<number>;
  const maxTaxRate: ValueBinding<number>;
  const areaTypes$: ValueBinding<TaxAreaType[]>;
  const areaTaxRates$: MapBinding<number, number>;
  const areaTaxIncomes$: MapBinding<number, number>;
  const areaTaxEffects$: MapBinding<number, number>;
  const areaResourceTaxRanges$: MapBinding<number, Bounds1>;
  const areaResources$: MapBinding<number, TaxResource[]>;
  const resourceTaxRates: MapBinding<TaxResource, number>;
  const resourceTaxIncomes: MapBinding<TaxResource, number>;
  const taxResourceInfos: MapBinding<TaxResource, TaxResourceInfo>;
  export interface TaxAreaType {
  	index: number;
  	id: string;
  	icon: string;
  	taxRateMin: number;
  	taxRateMax: number;
  	resourceTaxRateMin: number;
  	resourceTaxRateMax: number;
  	locked: boolean;
  }
  export interface TaxResource {
  	resource: number;
  	area: number;
  }
  export interface TaxResourceInfo {
  	id: string;
  	icon: string;
  }
  const setTaxRate: (rate: number) => void;
  const setAreaTaxRate: (area: number, rate: number) => void;
  const setResourceTaxRate: (resource: number, area: number, rate: number) => void;
  const activeTool$: ValueBinding<Tool>;
  const bulldozeConfirmationRequested$: EventBinding<unknown>;
  const availableSnapMask$: ValueBinding<number>;
  const selectedSnapMask$: ValueBinding<number>;
  const allSnapMask$: ValueBinding<number>;
  const snapOptionNames$: ValueBinding<string[]>;
  const elevationRange$: ValueBinding<Bounds1>;
  const elevation$: ValueBinding<number>;
  const elevationStep$: ValueBinding<number>;
  const parallelModeSupported$: ValueBinding<boolean>;
  const parallelMode$: ValueBinding<boolean>;
  const parallelOffset$: ValueBinding<number>;
  const contourMode$: ValueBinding<boolean>;
  const undergroundModeSupported$: ValueBinding<boolean>;
  const undergroundMode$: ValueBinding<boolean>;
  const elevationDownDisabled$: ValueBinding<boolean>;
  const elevationUpDisabled$: ValueBinding<boolean>;
  const colorSupported$: ValueBinding<boolean>;
  const color$: ValueBinding<Color>;
  const isEditor$: ValueBinding<boolean>;
  const replacingTrees$: ValueBinding<boolean>;
  const distance$: ValueBinding<number>;
  const distanceScale$: ValueBinding<number>;
  function setColor(color: Color): void;
  function selectTool(toolID: string): void;
  function selectToolMode(modeIndex: number): void;
  function confirmBulldoze(confirm: boolean): void;
  function changeElevation(dir: number): void;
  function elevationUp(): void;
  function elevationDown(): void;
  function elevationScroll(): void;
  function setElevationStep(step: number): void;
  function setSelectedSnapMask(mask: number): void;
  function toggleParallelMode(): void;
  function setParallelOffset(offset: number): void;
  function setContourMode(enabled: boolean): void;
  function setUndergroundMode(enabled: boolean): void;
  function setDistance(distance: number): void;
  const BULLDOZE_TOOL = "Bulldoze Tool";
  const DEFAULT_TOOL = "Default Tool";
  const ZONE_TOOL = "Zone Tool";
  const AREA_TOOL = "Area Tool";
  const NET_TOOL = "Net Tool";
  const OBJECT_TOOL = "Object Tool";
  const UPGRADE_TOOL = "Upgrade Tool";
  const TERRAIN_TOOL = "Terrain Tool";
  const SELECTION_TOOL = "Selection Tool";
  const ROUTE_TOOL = "Route Tool";
  const WATER_TOOL = "Water Tool";
  export interface Tool {
  	id: string;
  	modeIndex: number;
  	modes: ToolMode[];
  }
  export interface ToolMode {
  	id: string;
  	index: number;
  	icon: string;
  }
  const allowBrush$: ValueBinding<boolean>;
  const selectedBrush$: ValueBinding<Entity>;
  const brushes$: ValueBinding<Brush[]>;
  const brushSize$: ValueBinding<number>;
  const brushStrength$: ValueBinding<number>;
  const brushHeight$: ValueBinding<number>;
  const brushHeightMin$: ValueBinding<number>;
  const brushHeightMax$: ValueBinding<number>;
  const brushAngle$: ValueBinding<number>;
  const brushSizeMin$: ValueBinding<number>;
  const brushSizeMax$: ValueBinding<number>;
  export interface Brush {
  	entity: Entity;
  	name: string;
  	icon: string;
  	priority: number;
  }
  const selectBrush: (brush: Entity) => void;
  const setBrushSize: (size: number) => void;
  const setBrushStrength: (strength: number) => void;
  const setBrushAngle: (angle: number) => void;
  const setBrushHeight: (height: number) => void;
  const waterSimSpeed$: ValueBinding<number>;
  const simulateBackdropWater$: ValueBinding<boolean>;
  const showWaterSourceNames$: ValueBinding<boolean>;
  const setWaterSimSpeed: (value: number) => void;
  const setSimulateBackdropWater: (state: boolean) => void;
  const setShowWaterSourceNames: (state: boolean) => void;
  export interface ToolbarGroup {
  	entity: Entity;
  	children: ToolbarItem[];
  }
  export interface ToolbarItem {
  	entity: Entity;
  	name: string;
  	type: ToolbarItemType;
  	icon: string;
  	locked: boolean;
  	uiTag: string;
  	requirements: UnlockingRequirements;
  	highlight: boolean;
  	selectSound: UISound | string | null;
  	deselectSound: UISound | string | null;
  	shortcut: InputActionName;
  }
  export enum ToolbarItemType {
  	asset = 0,
  	menu = 1
  }
  export interface AssetCategory {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	uiTag: string;
  	highlight: boolean;
  }
  interface Asset$1 {
  	entity: Entity;
  	name: string;
  	priority: number;
  	icon: string;
  	dlc: string | null;
  	theme: string | null;
  	locked: boolean;
  	uiTag: string;
  	unique: boolean;
  	placed: boolean;
  	highlight: boolean;
  	constructionCost: NumericProperty | null;
  }
  interface Theme$1 {
  	entity: Entity;
  	name: string;
  	icon: string;
  	highlight: boolean;
  }
  export interface AssetPack {
  	entity: Entity;
  	name: string;
  	icon: string;
  	highlight: boolean;
  }
  export enum AgeMask {
  	Disabled = 0,
  	Child = 1,
  	Teen = 2,
  	Adult = 4,
  	Elderly = 8
  }
  const toolbarGroups$: ValueBinding<ToolbarGroup[]>;
  const assetCategories$: MapBinding<Entity, AssetCategory[]>;
  const assets$: MapBinding<Entity, Asset$1[]>;
  const themes$$1: ValueBinding<Theme$1[]>;
  const selectedThemes$: ValueBinding<Entity[]>;
  const vegetationAges$: ValueBinding<Theme$1[]>;
  const assetPacks$: ValueBinding<AssetPack[]>;
  const selectedAssetPacks$: ValueBinding<Entity[]>;
  const vanillaSelected$: ValueBinding<boolean>;
  const modsSelected$: ValueBinding<boolean>;
  const hasModAssets$: ValueBinding<boolean>;
  const selectedAssetMenu$: ValueBinding<Entity>;
  const selectedAssetCategory$: ValueBinding<Entity>;
  const selectedAsset$: ValueBinding<Entity>;
  const ageMask$: ValueBinding<number>;
  const decorationMode$: ValueBinding<boolean>;
  const setAgeMask: (ageMask: number) => void;
  const setSelectedThemes: (themes: Entity[]) => void;
  const setSelectedAssetPacks: (packs: Entity[]) => void;
  const setVanillaSelected: (vanillaSelected: boolean) => void;
  const setModsSelected: (modsSelected: boolean) => void;
  const selectAssetMenu: (assetMenu: Entity) => void;
  const selectAssetCategory: (assetCategory: Entity) => void;
  const selectAsset: (asset: Entity, updateTool: boolean) => void;
  const clearAssetSelection: () => void;
  const toggleToolOptions: (isActive: boolean) => void;
  const setDecorationMode: (decorationMode: boolean) => void;
  const CompareAssetsByPriority: (a: Asset$1, b: Asset$1) => number;
  const population$$1: ValueBinding<number>;
  const populationDelta$: ValueBinding<number>;
  const money$: ValueBinding<number>;
  const moneyDelta$: ValueBinding<number>;
  const cityName$: ValueBinding<string>;
  const unlimitedMoney$: ValueBinding<boolean>;
  const populationTrendThresholds$: ValueBinding<Number2>;
  const moneyTrendThresholds$: ValueBinding<Number2>;
  function setCityName(name: string): void;
  const passengerTypes$: ValueBinding<TransportType[]>;
  const cargoTypes$: ValueBinding<TransportType[]>;
  const transportLines$: ValueBinding<TransportLine[]>;
  const selectedPassengerType$: ValueBinding<string>;
  const selectedCargoType$: ValueBinding<string>;
  const selectLine: (entity: Entity) => void;
  const deleteLine: (entity: Entity) => void;
  const renameLine: (entity: Entity, name: string) => void;
  const setLineColor: (entity: Entity, color: Color) => void;
  const setLineActive: (entity: Entity, active: boolean) => void;
  const showLine: (entity: Entity, hideOthers: boolean) => void;
  const hideLine: (entity: Entity, showOthers: boolean) => void;
  const setLineSchedule: (entity: Entity, schedule: number) => void;
  const resetVisibility: () => void;
  const toggleHighlight: (entity: Entity) => void;
  const setSelectedPassengerType: (type: string) => void;
  const setSelectedCargoType: (type: string) => void;
  export interface TransportType {
  	id: string;
  	icon: string;
  	locked: boolean;
  	requirements: UnlockingRequirements;
  }
  export interface TransportLine {
  	name: Name | null;
  	vkName: Name | null;
  	lineData: TransportLineData;
  }
  export interface TransportLineData {
  	entity: Entity;
  	active: boolean;
  	visible: boolean;
  	isCargo: boolean;
  	color: Color;
  	schedule: number;
  	type: string;
  	length: number;
  	stops: number;
  	vehicles: number;
  	cargo: number;
  	usage: number;
  }
  export interface TransportStop {
  	name: Name;
  	entity: Entity;
  	position: number;
  	passengers: Entity[];
  }
  export type BalloonDirection = "up" | "down" | "left" | "right";
  export type BalloonAlignment = "start" | "center" | "end";
  export type CornerAlignment = "none" | "topLeft" | "topRight" | "bottomLeft" | "bottomRight";
  const tutorialsEnabled$: ValueBinding<boolean>;
  const tutorialIntroActive$: ValueBinding<boolean>;
  const activeTutorialList$: ValueBinding<TutorialList | null>;
  const nextTutorial$: ValueBinding<Entity>;
  const tutorialPending$: ValueBinding<Entity>;
  const tutorialCategories$: ValueBinding<AdvisorCategory[]>;
  const tutorialGroups$: MapBinding<Entity, AdvisorItem[]>;
  const tutorialListFocused$: {
  	readonly listeners: {
  		listener: BindingListener<boolean> | undefined;
  		set: (listener: BindingListener<boolean>) => void;
  		call: (newValue: boolean) => void;
  	}[];
  	disposed: boolean;
  	_value: boolean;
  	readonly registered: boolean;
  	readonly value: boolean;
  	subscribe: (listener?: BindingListener<boolean> | undefined) => {
  		readonly value: boolean;
  		setChangeListener: (listener: BindingListener<boolean>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: boolean) => void;
  };
  const setTutorialListFocused: (focused: boolean) => void;
  const tutorials$: MapBinding<Entity, Tutorial | null>;
  const activeTutorial$: ValueBinding<Tutorial | null>;
  const activeTutorialPhase$: ValueBinding<TutorialPhase | null>;
  const listIntroActive$: ValueBinding<boolean>;
  const listOutroActive$: ValueBinding<boolean>;
  const activateTutorial: (tutorial: Entity) => void;
  const activateTutorialPhase: (tutorial: Entity, phase: Entity) => void;
  const forceTutorial: (tutorial: Entity, phase: Entity, advisorActivation: boolean) => void;
  const toggleTutorialListFocus: (state?: boolean) => void;
  const completeActiveTutorialPhase: () => void;
  const completeActiveTutorial: () => void;
  const completeIntro: (tutorialsEnabled: boolean) => void;
  const completeListIntro: () => void;
  const completeListOutro: () => void;
  const tutorialAutoplay$: ValueBinding<boolean>;
  const setTutorialAutoplay: (autoplay: boolean) => void;
  const activateTutorialTag: (tag: string, active: boolean) => void;
  const triggerTutorialTag: (trigger: string) => void;
  const untriggerTutorialTag: (trigger: string) => void;
  const useTutorialTagActivation: (uiTag: string | undefined, active?: boolean) => void;
  const useTutorialTagTrigger: (uiTag: string | undefined, active?: boolean) => void;
  function useTutorialTag(uiTag: string | undefined, active?: boolean): void;
  const advisorPanelVisible$: {
  	readonly listeners: {
  		listener: BindingListener<boolean> | undefined;
  		set: (listener: BindingListener<boolean>) => void;
  		call: (newValue: boolean) => void;
  	}[];
  	disposed: boolean;
  	_value: boolean;
  	readonly registered: boolean;
  	readonly value: boolean;
  	subscribe: (listener?: BindingListener<boolean> | undefined) => {
  		readonly value: boolean;
  		setChangeListener: (listener: BindingListener<boolean>) => void;
  		dispose(): void;
  	};
  	dispose: () => void;
  	update: (newValue: boolean) => void;
  };
  function toggleAdvisorPanel(state?: boolean): void;
  export interface AdvisorCategory {
  	entity: Entity;
  	name: string;
  	shown: boolean;
  	force: boolean;
  	locked: boolean;
  	children: AdvisorItem[];
  }
  export enum AdvisorItemType {
  	Tutorial = 0,
  	Group = 1
  }
  export interface AdvisorItem {
  	entity: Entity;
  	name: string;
  	icon: string | null;
  	type: AdvisorItemType;
  	shown: boolean;
  	force: boolean;
  	locked: boolean;
  	children: AdvisorItem[];
  }
  export interface TutorialList {
  	entity: Entity;
  	name: string;
  	tutorials: Tutorial[];
  	hints: Tutorial[];
  	intro: boolean;
  	hintsVisible: boolean;
  	disabledVisible: boolean;
  	enableAutoCollapse: boolean;
  	enableAutoExpand: boolean;
  	autoplayDefault: boolean;
  }
  export interface Tutorial {
  	entity: Entity;
  	name: string;
  	icon: string;
  	locked: boolean;
  	priority: number;
  	active: boolean;
  	completed: boolean;
  	shown: boolean;
  	force: boolean;
  	mandatory: boolean;
  	advisorActivation: boolean;
  	phases: TutorialPhase[];
  	filters: string[] | null;
  	alternatives: Entity[] | null;
  	category: TutorialCategory;
  }
  export enum TutorialControlScheme {
  	KeyboardAndMouse = 1,
  	Gamepad = 2,
  	All = 3
  }
  export interface TutorialPhase {
  	entity: Entity;
  	name: string;
  	type: TutorialPhaseType;
  	active: boolean;
  	shown: boolean;
  	force: boolean;
  	completed: boolean;
  	forcesCompletion: boolean;
  	isBranch: boolean;
  	image: string | null;
  	infoPanelImages: string[] | null;
  	overrideImagePS: string | null;
  	overrideImageXbox: string | null;
  	icon: string | null;
  	titleVisible: boolean;
  	descriptionVisible: boolean;
  	balloonTargets: BalloonUITarget[];
  	highlightTargets: UIHighlightTarget[];
  	controlScheme: TutorialControlScheme;
  	trigger: TutorialTrigger | null;
  	scrollable: boolean;
  	autoScroll: boolean;
  }
  export interface BalloonUITarget {
  	uiTag: string;
  	direction: BalloonDirection;
  	alignment: BalloonAlignment;
  	cornerAlignment: CornerAlignment;
  	hideArrow: boolean;
  	highlightUiElement: boolean;
  }
  export interface UIHighlightTarget {
  	uiTag: string;
  	highlightUiElement: boolean;
  }
  export interface TutorialTrigger {
  	entity: Entity;
  	name: string;
  	blinkTags: string[][];
  	displayUI: boolean;
  	active: boolean;
  	completed: boolean;
  	preCompleted: boolean;
  	phaseBranching: boolean;
  }
  export enum TutorialPhaseType {
  	Balloon = 0,
  	Card = 1,
  	CenterCard = 2,
  	InfoPanel = 3
  }
  export enum TutorialCategory {
  	None = 0,
  	ZoneBasics = 1,
  	UtilityBasics = 2
  }
  const upgrades$: MapBinding<Entity, Asset$1[]>;
  const upgradeDetails$: MapBinding<Entity, PrefabDetails | null>;
  const selectedUpgrade$: ValueBinding<Entity>;
  const upgrading$: ValueBinding<boolean>;
  function selectUpgrade(entity: Entity, upgradeEntity: Entity): void;
  function clearUpgradeSelection(): void;
  // https://coherent-labs.com/Documentation/cpp-gameface/d1/dea/shape_morphing.html
  // https://coherent-labs.com/Documentation/cpp-gameface/d4/d08/interface_morph_animation.html
  export export interface HTMLImageElement {
  	getSrcSVGAnimation(): MorphAnimation | null;
  }
  export export interface Element {
  	getMaskSVGAnimation(): MorphAnimation | null;
  }
  export export interface MorphAnimation {
  	currentTime: number;
  	playbackRate: number;
  	play(): void;
  	pause(): void;
  	reverse(): void;
  	playFromTo(playTime: number, pauseTime: number, callback?: () => void): void;
  }
  
  export namespace upgrade {
  	export { clearUpgradeSelection, selectUpgrade, selectedUpgrade$, upgradeDetails$, upgrades$, upgrading$ };
  }
  export namespace budget {
  	export { ServiceBudget, ServiceBuildingBudgetData, ServiceBuildingBudgetInfo, ServiceFee };
  }
  export namespace camera {
  	export { focusEntity, focusedEntity$ };
  }
  export namespace chirper {
  	export { Chirp, ChirpLink, ChirpSender, addLike, chirpAdded$, chirps$, removeLike, selectLink };
  }
  export namespace cinematic {
  	export { CinematicCameraAsset, CinematicCameraCurveModifier, addKeyFrame, availableCloudTargets$, captureKey, cinematicCameraSequenceAssets$, deleteCinematicCameraSequence, getControllerDelta, getControllerPanDelta, getControllerZoomDelta, group, lastLoadedCinematicCameraSequence$, loadCinematicCameraSequence, loop$, modifierAnimationCurveData$, moveKeyFrame, onAfterPlaybackDurationChange, playbackDuration$, playing$, removeCameraTransformKey, removeKeyFrame, resetCinematicCameraSequence, saveCinematicCameraSequence, selectCloudTarget, selectedCloudTarget$, setPlaybackDuration, setTimelinePosition, stopPlayback, timelineLength$, timelinePosition$, toggleCurveEditorFocus, toggleLoop, togglePlayback, transformAnimationCurveData$, useCinematicCameraBindings };
  }
  export namespace cityInfo {
  	export { Factor, commercialDemand$, commercialFactors$, happiness$, happinessFactors$, industrialDemand$, industrialFactors$, officeDemand$, officeFactors$, residentialHighDemand$, residentialHighFactors$, residentialLowDemand$, residentialLowFactors$, residentialMediumDemand$, residentialMediumFactors$ };
  }
  export namespace climate {
  	export { Season, WeatherType, seasonName$, temperature$, weather$ };
  }
  export namespace economyBudget {
  	export { BudgetItem, BudgetSource, expenseItems$, expenseValues$, getItemValue, incomeItems$, incomeValues$, totalExpenses$, totalIncome$ };
  }
  export namespace loan {
  	export { Loan, acceptLoanOffer, currentLoan$, loanLimit$, loanOffer$, requestLoanOffer, resetLoanOffer };
  }
  export namespace production {
  	export { FinalConsumer, FinalConsumption, FinalConsumptionQuery, ProductionChainData, ProductionLink, Resource, ResourceCategory, ResourceData, ResourceDetails, ResourceValue, Service, consumptionProduction$, finalConsumption$, importExport$, maxProduction$, maxProgress$, productionChainData$, resourceCategories$, resourceDetails$, resources$, selectedResource$, selectedResourceCategory$, serviceUpkeepConsumption$, services$, storedResource$ };
  }
  export namespace service {
  	export { Service$1 as Service, ServiceDetails, ServiceFee$1 as ServiceFee, resetService, serviceDetails$, services$$1 as services$, setServiceBudget, setServiceFee };
  }
  export namespace event$1 {
  	export { EventData, EventInfo, eventMap$, events$, onCloseJournal, onOpenJournal };
  }
  export namespace game {
  	export { ChirperPanel, CinematicCameraPanel, CityInfoPanel, CityInfoPanelTab, EconomyPanel, EconomyPanelTab, GamePanel, GamePanelType, GamePanels, GameScreen, GlossaryPanel, GlossaryPanelTab, InfoviewMenu, JournalPanel, LayoutPosition, LifePathPanel, ModsMenuPanel, NotificationsPanel, PhotoModePanel, ProgressionPanel, ProgressionPanelTab, RadioPanel, StatisticsPanel, TabbedGamePanel, TransportationOverviewPanel, TransportationOverviewPanelTab, activeGamePanel$, activeGameScreen$, activePanelPosition$, blockingPanelActive$, canUseSaveSystem$, closeActiveGamePanel, closeGamePanel, setActiveGameScreen, setGlossaryCategory, showCityInfoPanel, showEconomyPanel, showFreeCameraScreen, showGamePanel, showGlossaryPanel, showLifePathDetail, showLifePathList, showMainScreen, showPauseScreen, showProgressionPanel, showTransportationOverviewPanel, toggleGamePanel, toggleInfoviewMenu, toggleLifePathPanel, toggleRadioPanel, toggleTransportationOverviewPanel };
  }
  export namespace infoview {
  	export { CargoSummary, ChartData, IndicatorValue, PassengerSummary, TransportSummaries, activeInfoview$, ageData$, arrestedCriminals$, attractiveness$, availableFertility$, availableFish$, availableForest$, availableOil$, availableOre$, averageAirPollution$, averageCrimeProbability$, averageFees$, averageFireHazard$, averageGroundPollution$, averageHealth$, averageHotelPrice$, averageIncome$, averageLandValue$, averageNoisePollution$, averageRent$, averageResourceCost$, averageUpkeep$, averageWaterPollution$, averageWealth$, batteryCharge$, bikeParking$, bikeParkingAvailability$, birthRate$, cemeteryAvailability$, cemeteryCapacity$, cemeteryUse$, clearActiveInfoview, closeInfoviewMenu, collectedMail$, collegeAvailability$, collegeCapacity$, collegeEligible$, collegeStudents$, commercialLevels$, commercialProfitability$, crimePerMonth$, crimeProbability$, crimeProducers$, criminals$, deathRate$, deathcareAvailability$, deliveredMail$, educationData$, electricityAvailability$, electricityConsumption$, electricityExport$, electricityImport$, electricityProduction$, electricityTrade$, electricityTransmission$, electricityTransmitted$, elementaryAvailability$, elementaryCapacity$, elementaryEligible$, elementaryStudents$, employed$, employeesData$, escapedRate$, fertilityExtractionRate$, fertilityRenewalRate$, fishExtractionRate$, fishRenewalRate$, forestExtractionRate$, forestRenewalRate$, garbageProcessingRate$, garbageProductionRate$, healthcareAvailability$, highSchoolAvailability$, highSchoolCapacity$, highSchoolEligible$, highSchoolStudents$, homeless$, homelessness$, inJail$, inPrison$, industrialLevels$, industrialProfitability$, infoviews$, jailAvailability$, jailCapacity$, jobs$, landfillAvailability$, landfillCapacity$, mailProductionRate$, movedAway$, movedIn$, officeLevels$, officeProfitability$, oilExtractionRate$, oreExtractionRate$, parkedCars$, parkingAvailability$, parkingCapacity$, parkingIncome$, patientCapacity$, patientCount$, population$, postServiceAvailability$, prisonAvailability$, prisonCapacity$, prisoners$, processingAvailability$, processingRate$, residentialLevels$, setActiveInfoview, setInfomodeActive, setZoneAlpha, sewageAvailability$, sewageCapacity$, sewageConsumption$, sewageExport$, shelterAvailability$, shelterCapacity$, shelteredCount$, sickCount$, storedGarbage$, topExportColors$, topExportData$, topExportNames$, topImportColors$, topImportData$, topImportNames$, tourismRate$, trafficFlow$, transportSummaries$, unemployment$, universityAvailability$, universityCapacity$, universityEligible$, universityStudents$, useInfoviewToggle, waterAvailability$, waterCapacity$, waterConsumption$, waterExport$, waterImport$, waterTrade$, weatherEffect$, workers$, workplacesData$, worksplaces$, zoneInfos$ };
  }
  export namespace infoviewTypes {
  	export { ActiveInfoview, Infomode, InfomodeColorLegend, InfomodeGradientLegend, Infoview, ZoneInfo };
  }
  export namespace life {
  	export { FollowedCitizen, LifePathDetails, LifePathEvent, LifePathItem, LifePathItemType, LifePathItems, followCitizen, followedCitizens$, lifePathDetails$, lifePathItems$, maxFollowedCitizens$, unfollowCitizen };
  }
  export namespace map {
  	export { MapTileResource, MapTileStatus, availableWater$, buildableLand$, disableMapTileView, mapTilePanelVisible$, mapTileViewActive$, permitCost$, permits$, purchaseFlags$, purchaseMapTiles, purchasePrice$, purchaseUpkeep$, resources$$1 as resources$, setMapTileViewActive };
  }
  export namespace photo {
  	export { PhotoModeWidget, PhotoModeWidgets, PhotoWidgetType, Tab, adjustments$, cinematicCameraVisible$, group$1 as group, orbitCameraActive$, overlayHidden$, resetCamera, root, selectTab, selectedTab$, setOverlayHidden, tabs$, takeScreenshot, toggleCinematicCamera, toggleOrbitCameraActive };
  }
  export namespace policy {
  	export { PolicyData, PolicySliderData, PolicySliderProp, cityPolicies$, setCityPolicy, setPolicy };
  }
  export namespace prefab {
  	export { ManualUITagsConfiguration, PrefabDetails, Theme, UnlockingRequirements, emptyPrefabDetails, manualUITags$, prefabDetails$, themes$ };
  }
  export namespace prefabEffects {
  	export { AdjustHappinessEffect, CityModifier, CityModifierEffect, CityModifierType, LeisureProvider, LeisureProviderEffect, LeisureType, LocalModifier, LocalModifierEffect, LocalModifierType, PrefabEffect, PrefabEffectType, PrefabEffects };
  }
  export namespace prefabProperties {
  	export { CONSUMPTION_PROPERTY, ConsumptionProperty, ELECTRICITY_PROPERTY, ElectricityProperty, POLLUTION_PROPERTY, Pollution, PollutionProperty, PrefabProperties, PrefabProperty, TRANSPORT_STOP_PROPERTY, TransportStopProperty, UPKEEPNUMBER2_PROPERTY, UPKEEPNUMBER_PROPERTY, UpkeepNumber2Property, UpkeepNumberProperty, Voltage };
  }
  export namespace prefabRequirements {
  	export { AreaType, CitizenRequirement, DevTreeNodeRequirement, MilestoneRequirement, ObjectBuiltRequirement, PrefabRequirement, PrefabRequirementType, PrefabRequirements, ProcessingRequirement, StrictObjectBuiltRequirement, TransportRequirement, TutorialRequirement, UnlockRequirement, ZoneBuiltRequirement };
  }
  export namespace devTree {
  	export { DevTreeNode, DevTreeNodeDetails, DevTreeService, DevTreeServiceDetails, devPoints$, nodeDetails$, nodes$, purchaseNode, selectedDevTree$, selectedNode$, serviceDetails$$1 as serviceDetails$, services$$2 as services$ };
  }
  export namespace feature {
  	export { UnlockingProps, isFeatureLocked, lockedFeatures$, useFeatureLocked, useFeatureUnlocking };
  }
  export namespace milestone {
  	export { Asset, Feature, Milestone, MilestoneDetails, MilestoneUnlock, MilestoneUnlockType, MilestoneUnlocks, Policy, Service$2 as Service, UnlockDetails, XpMessage, achievedMilestone$, achievedMilestoneXP$, clearUnlockedMilestone, defaultMilestoneDetails, maxMilestoneReached$, milestoneDetails$, milestoneUnlocks$, milestones$, nextMilestoneXP$, reachedPopulationGoal$, totalXP$, unlockAll$, unlockDetails$, unlockedMilestone$, victoryPopupShown$, xpMessageAdded$ };
  }
  export namespace signatureBuilding {
  	export { removeUnlockedSignature, unlockedSignatures$ };
  }
  export namespace radio {
  	export { RadioClip, RadioNetwork, RadioProgram, RadioStation, currentSegment$, emergencyFocusable$, emergencyMessage$, emergencyMode$, focusEmergency, muted$, networks$, paused$, playNext, playPrevious, radioEnabled$, segmentChanged$, selectNetwork, selectStation, selectedNetwork$, selectedStation$, setMuted, setPaused, setSkipAds, setVolume, skipAds$, stations$, toggleMuted, togglePaused, toggleSkipAds, volume$ };
  }
  export namespace selectedInfo {
  	export { ActionsSection, AnimalSection, AttractivenessFactor, AttractivenessSection, AverageHappinessSection, BatterySection, CapacityInfo, CargoSection, CargoTransportVehicleSection, CitizenSection, ColorSection, ColorSet, ComfortSection, CompanySection, ContentPrerequisiteSection, DeathcareSection, DeathcareVehicleSection, DeliveryVehicleSection, DescriptionSection, DestroyedBuildingSection, DestroyedTreeSection, DeveloperSection, DeveloperSubsection, DeveloperSubsectionType, DeveloperSubsections, DispatchedVehiclesSection, District, DistrictsSection, DummyHumanSection, EducationSection, EfficiencyFactor, EfficiencySection, ElectricitySection, EmployeesSection, ExtractorVehicleSection, FireSection, FireVehicleSection, GarbageSection, GarbageVehicleSection, GenericInfo, HealthcareSection, HealthcareVehicleSection, HouseholdSidebarItem, HouseholdSidebarSection, HouseholdSidebarVariant, HouseholdWealthData, InfoList, Item, LINE_STOP, LINE_VEHICLE, LevelSection, Line, LineItem, LineSection, LineSegment, LineStop, LineType, LineVehicle, LineVisualizerSection, LinesSection, LoadSection, LocalServiceBuilding, LocalServicesSection, Location$1 as Location, MailSection, MailSectionType, MaintenanceVehicleSection, NotificationsSection, ParkSection, ParkingSection, PassengersSection, PdxModsSection, PoliceSection, PoliceVehicleSection, PoliciesSection, Pollution$1 as Pollution, PollutionSection, PostVehicleSection, PrisonSection, PrivateVehicleSection, ProfitabilitySection, PublicTransportVehicleSection, ResidentsSection, Resource$1 as Resource, ResourceSection, RoadSection, ScheduleSection, SectionType, SelectVehiclesSection, SelectedInfoPanelTab, SelectedInfoSection, SelectedInfoSectionBase, SelectedInfoSectionProps, SelectedInfoSections, SewageSection, ShelterSection, StatusSection, StorageSection, TicketPriceSection, TitleSection, TradedResourcesSection, TransformerSection, Upgrade, UpgradeInfo, UpgradePropertiesSection, UpgradeType, UpgradesSection, UpkeepItem, UpkeepSection, Vehicle, VehicleCountSection, VehiclePrefab, VehicleSectionProps, VehicleWithLineSectionProps, VehiclesSection, VisualCustomizeSection, WaterSection, activeSelection$, bottomSections$, clearSelection, developerSection$, householdSidebarSection$, lineVisualizerSection$, middleSections$, selectEntity, selectedEntity$, selectedInfoPosition$, selectedRoute$, selectedTrailerController$, selectedUITag$, setSelectedRoute, titleSection$, tooltipTags$, topSections$, useGeneratedTooltipParagraphs, useTooltipParagraph, useTooltipParagraphs };
  }
  export namespace statistics {
  	export { StatCategory, StatItem, activeCategory$, activeGroup$, addStat, addStatChildren, clearStats, removeStat, sampleCount$, sampleRange$, selectedStatistics$, setSampleRange, stacked$, statGroupsMap$, statLabels$, statUnlockingRequirements$, statisticsCategories$, statsData$, updatesPerDay$ };
  }
  export namespace taxation {
  	export { TaxAreaType, TaxResource, TaxResourceInfo, areaResourceTaxRanges$, areaResources$, areaTaxEffects$, areaTaxIncomes$, areaTaxRates$, areaTypes$, maxTaxRate, minTaxRate, resourceTaxIncomes, resourceTaxRates, setAreaTaxRate, setResourceTaxRate, setTaxRate, taxEffect, taxIncome, taxRate, taxResourceInfos };
  }
  export namespace time {
  	export { LightingState, SimulationDate, SimulationDateTime, SimulationTime, TimeSettings, calculateDateFromDays, calculateDateFromTicks, calculateDateTimeFromTicks, calculateMinutesSinceMidnightFromTicks, calculateTimeFromMinutesSinceMidnight, dateEquals, day$, lightingState$, setSimulationPaused, setSimulationSpeed, simulationPaused$, simulationPausedBarrier$, simulationSpeed$, ticks$, timeSettings$ };
  }
  export namespace tool {
  	export { AREA_TOOL, BULLDOZE_TOOL, Brush, DEFAULT_TOOL, NET_TOOL, OBJECT_TOOL, ROUTE_TOOL, SELECTION_TOOL, TERRAIN_TOOL, Tool, ToolMode, UPGRADE_TOOL, WATER_TOOL, ZONE_TOOL, activeTool$, allSnapMask$, allowBrush$, availableSnapMask$, brushAngle$, brushHeight$, brushHeightMax$, brushHeightMin$, brushSize$, brushSizeMax$, brushSizeMin$, brushStrength$, brushes$, bulldozeConfirmationRequested$, changeElevation, color$, colorSupported$, confirmBulldoze, contourMode$, distance$, distanceScale$, elevation$, elevationDown, elevationDownDisabled$, elevationRange$, elevationScroll, elevationStep$, elevationUp, elevationUpDisabled$, isEditor$, parallelMode$, parallelModeSupported$, parallelOffset$, replacingTrees$, selectBrush, selectTool, selectToolMode, selectedBrush$, selectedSnapMask$, setBrushAngle, setBrushHeight, setBrushSize, setBrushStrength, setColor, setContourMode, setDistance, setElevationStep, setParallelOffset, setSelectedSnapMask, setShowWaterSourceNames, setSimulateBackdropWater, setUndergroundMode, setWaterSimSpeed, showWaterSourceNames$, simulateBackdropWater$, snapOptionNames$, toggleParallelMode, undergroundMode$, undergroundModeSupported$, waterSimSpeed$ };
  }
  export namespace toolbar$1 {
  	export { AgeMask, Asset$1 as Asset, AssetCategory, AssetPack, CompareAssetsByPriority, Theme$1 as Theme, ToolbarGroup, ToolbarItem, ToolbarItemType, ageMask$, assetCategories$, assetPacks$, assets$, clearAssetSelection, decorationMode$, hasModAssets$, modsSelected$, selectAsset, selectAssetCategory, selectAssetMenu, selectedAsset$, selectedAssetCategory$, selectedAssetMenu$, selectedAssetPacks$, selectedThemes$, setAgeMask, setDecorationMode, setModsSelected, setSelectedAssetPacks, setSelectedThemes, setVanillaSelected, themes$$1 as themes$, toggleToolOptions, toolbarGroups$, vanillaSelected$, vegetationAges$ };
  }
  export namespace toolbarBottom {
  	export { cityName$, money$, moneyDelta$, moneyTrendThresholds$, population$$1 as population$, populationDelta$, populationTrendThresholds$, setCityName, unlimitedMoney$ };
  }
  export namespace transport {
  	export { TransportLine, TransportLineData, TransportStop, TransportType, cargoTypes$, deleteLine, hideLine, passengerTypes$, renameLine, resetVisibility, selectLine, selectedCargoType$, selectedPassengerType$, setLineActive, setLineColor, setLineSchedule, setSelectedCargoType, setSelectedPassengerType, showLine, toggleHighlight, transportLines$ };
  }
  export namespace tutorial {
  	export { AdvisorCategory, AdvisorItem, AdvisorItemType, BalloonUITarget, Tutorial, TutorialCategory, TutorialControlScheme, TutorialList, TutorialPhase, TutorialPhaseType, TutorialTrigger, UIHighlightTarget, activateTutorial, activateTutorialPhase, activateTutorialTag, activeTutorial$, activeTutorialList$, activeTutorialPhase$, advisorPanelVisible$, completeActiveTutorial, completeActiveTutorialPhase, completeIntro, completeListIntro, completeListOutro, forceTutorial, listIntroActive$, listOutroActive$, nextTutorial$, setTutorialAutoplay, setTutorialListFocused, toggleAdvisorPanel, toggleTutorialListFocus, triggerTutorialTag, tutorialAutoplay$, tutorialCategories$, tutorialGroups$, tutorialIntroActive$, tutorialListFocused$, tutorialPending$, tutorials$, tutorialsEnabled$, untriggerTutorialTag, useTutorialTag, useTutorialTagActivation, useTutorialTagTrigger };
  }
  
  export {
  	budget,
  	camera,
  	chirper,
  	cinematic,
  	cityInfo,
  	climate,
  	devTree,
  	economyBudget,
  	event$1 as event,
  	feature,
  	game,
  	infoview,
  	infoviewTypes,
  	life,
  	loan,
  	map,
  	milestone,
  	photo,
  	policy,
  	prefab,
  	prefabEffects,
  	prefabProperties,
  	prefabRequirements,
  	production,
  	radio,
  	selectedInfo,
  	service,
  	signatureBuilding,
  	statistics,
  	taxation,
  	time,
  	tool,
  	toolbar$1 as toolbar,
  	toolbarBottom,
  	transport,
  	tutorial,
  	upgrade,
  };
  
  export {};
  
}

// ---------------------------------------------------------------------------------------------
// AGORA — dashboard binding payloads.
//
// Authority: docs/contracts/ui_bindings.md (schemaVersion 2). FROZEN for M4 — do not rename a
// field, add a field, or change a sort key here without changing that document first. Nothing
// checks this contract at compile time: a mismatch renders an empty panel, not a build error.
//
// These are GLOBAL ambient declarations. Reference them as `Agora.SeatRow` with NO import
// statement. Do not `import { SeatRow } from "..."` — there is no runtime module behind these
// types, and `isolatedModules` would emit the import and fail webpack resolution.
//
//   const seats$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);
//   const seats  = useValue(seats$);
//
// Wire conventions (see section 2 of the contract):
//   - SimDate is the string "YYYY-MM-DD"; an ABSENT date is "" — never null.
//   - An absent id is "" — never null. Only the four documented seat payloads are nullable objects.
//   - Enums arrive as the C# member NAME, never the integer.
//   - Shares / rates / indices / progress are in [0,1]. Happiness is in [0,100].
//   - Every list arrives pre-sorted by a documented key. Do not re-sort in the panel.
//
// HAZARD: this file is regenerated by `npm run update` (create-csii-ui-mod). If that is ever run,
// this block is lost — move it to ui/types/agora.d.ts and update section 9 of the contract.
// ---------------------------------------------------------------------------------------------

declare namespace Agora {
  // -- primitives ------------------------------------------------------------------------------

  /** A SimDate on the wire: "YYYY-MM-DD". An absent date is the empty string, never null. */
  type SimDateString = string;

  /** An entity id on the wire. An absent id is the empty string, never null. */
  type IdString = string;

  // -- enum names (C# member names, never integers) --------------------------------------------

  type IssueName =
    | "Services" | "CostOfLiving" | "Environment" | "Transit" | "Growth" | "HeritageOrder";

  type WealthTierName = "Low" | "Middle" | "High";

  type EducationTierName =
    | "Uneducated" | "PoorlyEducated" | "Educated" | "WellEducated" | "HighlyEducated";

  type AgeBandName = "Child" | "Teen" | "Adult" | "Elderly";

  type ElectoralSystemName = "Proportional" | "FirstPastThePost";

  type RegionThemeName = "Eu" | "Na";

  type PartyStatusName = "Active" | "Endangered" | "Dissolved" | "Merged" | "Revived";

  type FactionStatusName = "Active" | "Endangered" | "Dissolved" | "Merged" | "Revived";

  type CoalitionStatusName = "Negotiating" | "Governing" | "Minority" | "Collapsed" | "Expired";

  type CoalitionCollapseReasonName =
    | "None" | "StabilityDecay" | "MandateFailure" | "EventShock" | "IdeologicalDrift"
    | "PartnerWithdrawal";

  type MandateStatusName =
    | "Pending" | "Active" | "Fulfilled" | "PartiallyFulfilled" | "Defied" | "Abandoned";

  type MandateDirectionName = "Increase" | "Decrease";

  type MandateMetricName =
    | "Happiness" | "Unemployment" | "AirPollution" | "GroundPollution" | "NoisePollution"
    | "WaterPollution" | "CrimeRate" | "HealthCoverage" | "EducationCoverage" | "PoliceCoverage"
    | "FireCoverage" | "GarbageCoverage" | "TransitCoverage" | "AverageCommuteMinutes"
    | "AverageRent" | "AverageLandValue" | "RentBurden" | "Population" | "BudgetBalance" | "Debt";

  type EventRegionName = "Eu" | "Na" | "Global";

  type EventOriginName = "Catalog" | "Procedural" | "Political";

  /** What produced a feed item. Drives the icon, not the layout. */
  type NewsKindName = "Article" | "Event" | "Election" | "Coalition" | "Mandate" | "Party";

  /**
   * Engine-authored failure code for the flavor provider. NEVER LLM output and never a raw
   * exception message — the UI may switch on it. "" means the last attempt succeeded.
   */
  type FlavorErrorName = "" | "CliMissing" | "Timeout" | "BadJson" | "Disabled" | "Unknown";

  // -- agora.state -----------------------------------------------------------------------------

  /**
   * `agora.state.summary` — ValueBinding, republished monthly and on every election.
   * Empty value: EMPTY_STATE_SUMMARY (see section 6 of the contract).
   */
  interface StateSummary {
    /** PoliticalState.SchemaVersion. 0 before the first publish. */
    schemaVersion: number;
    /** Current sim date. "" before the first publish. */
    date: SimDateString;
    termNumber: number;
    system: ElectoralSystemName;
    theme: RegionThemeName;
    /** "" when no election is scheduled. */
    nextElectionDate: SimDateString;
    isCampaignSeason: boolean;
    /** -1 when no election is scheduled — 0 means "this week". */
    weeksToElection: number;
    /** "" when the city has no mayor (normal under a pure list system). */
    mayorPartyId: IdString;
  }

  // -- agora.parties ---------------------------------------------------------------------------

  /**
   * `agora.parties.roster` — the lookup table. Every other payload identifies a party by id only;
   * name and colour are resolved here so two panels cannot disagree about a party's colour.
   * Sorted by `id` ordinal ascending. Empty value: [].
   *
   * `name` and `shortName` are FLAVOR — render them, never parse or sort by them.
   */
  interface PartyBrief {
    id: IdString;
    name: string;
    shortName: string;
    /** "#RRGGBB". Engine-owned, from the tuned palette. */
    colorHex: string;
    status: PartyStatusName;
    isIncumbent: boolean;
    isInGovernment: boolean;
    coreGrievance: IssueName;
    foundedDate: SimDateString;
    /** "" while the party still exists. */
    dissolvedDate: SimDateString;
  }

  /**
   * `agora.parties.factions` — sorted by `partyId` ascending, then `internalSupport` DESCENDING,
   * then `id` ascending. Empty value: []. Empty under the EU theme, which has no factions.
   *
   * `name`, `shortName` and `leaderName` are FLAVOR.
   */
  interface FactionBrief {
    id: IdString;
    partyId: IdString;
    name: string;
    shortName: string;
    leaderName: string;
    /** [0,1]. The factions of one party sum to 1. */
    internalSupport: number;
    isDominant: boolean;
    /** [0,1]. High tension precedes a split. */
    tensionWithParty: number;
    status: FactionStatusName;
    coreGrievance: IssueName;
  }

  // -- shared ----------------------------------------------------------------------------------

  /**
   * One party's slice of a vote. EVERY list of these — wherever it appears — is sorted by
   * `partyId` ordinal ascending. That is the engine contract for List<PartyVoteShare>, not a
   * stylistic choice, and the panel must not re-sort it.
   */
  interface PartyShare {
    partyId: IdString;
    /** [0,1]. */
    share: number;
  }

  // -- agora.seats -----------------------------------------------------------------------------

  /**
   * `agora.seats.allocation` — sorted by `seats` DESCENDING, then `partyId` ascending so the chart
   * does not reshuffle on ties. Empty value: [].
   *
   * `listSeats` is 0 for every row under FPTP; `districtSeats` is 0 under a pure list system.
   * Branch on `StateSummary.system`, never on which field happens to be zero.
   */
  interface SeatRow {
    partyId: IdString;
    seats: number;
    /** [0,1]. */
    seatShare: number;
    /** [0,1]. */
    voteShare: number;
    districtSeats: number;
    listSeats: number;
    passedThreshold: boolean;
  }

  /**
   * `agora.seats.government` — the governing coalition. Under FPTP the winning party plus mayor is
   * also modelled as a coalition, so this is populated under both systems and the panel needs one
   * code path. Empty value: null (no government formed yet, or negotiations collapsed).
   */
  interface GovernmentSummary {
    id: IdString;
    status: CoalitionStatusName;
    leadPartyId: IdString;
    /** Sorted ordinal ascending. Includes the lead party. */
    memberPartyIds: IdString[];
    /** Sorted ordinal ascending. */
    oppositionPartyIds: IdString[];
    seats: number;
    /** [0,1]. */
    seatShare: number;
    hasMajority: boolean;
    /** [0,1]. Ideological agreement between partners. */
    cohesion: number;
    /** [0,1]. Decays; at 0 the coalition collapses. */
    stability: number;
    /** "None" while governing. */
    collapseReason: CoalitionCollapseReasonName;
    formedDate: SimDateString;
    /** "" while the coalition is still in office. */
    endedDate: SimDateString;
    formationAttempts: number;
    electionId: IdString;
    /** Ids into `agora.news.mandates`. Sorted ordinal ascending. */
    mandateIds: IdString[];
  }

  /** `agora.seats.mayor` — empty value: null. `name` is FLAVOR. */
  interface MayorSummary {
    partyId: IdString;
    name: string;
    electionId: IdString;
    sinceDate: SimDateString;
    /** [0,1]. Winning margin over the runner-up. */
    margin: number;
    /** Sorted by partyId ascending. */
    voteShares: PartyShare[];
  }

  /** `agora.seats.lastElection` — empty value: null (no election has been held yet). */
  interface ElectionSummary {
    id: IdString;
    date: SimDateString;
    system: ElectoralSystemName;
    termNumber: number;
    isSnapElection: boolean;
    /** [0,1]. */
    turnout: number;
    totalSeats: number;
    totalVotesCast: number;
    totalEligibleVoters: number;
    /** Mean absolute deviation of the final poll from the result, in share points [0,1]. */
    finalPollDeviation: number;
    nextElectionDate: SimDateString;
    /** Sorted by partyId ascending. */
    cityVoteShares: PartyShare[];
  }

  /**
   * `agora.seats.history` — sorted by `date` DESCENDING, then `id` ascending.
   * Capped at 12 rows (AGORA_ELECTION_HISTORY_MAX). Empty value: [].
   */
  interface ElectionHistoryRow {
    id: IdString;
    date: SimDateString;
    termNumber: number;
    isSnapElection: boolean;
    /** [0,1]. */
    turnout: number;
    winningPartyId: IdString;
    /** "" when the election produced no mayor. */
    mayorPartyId: IdString;
    totalSeats: number;
  }

  /**
   * `agora.seats.latestPoll` — the most recently PUBLISHED poll. Empty value: null.
   *
   * `shares` is the published figure. PollResult.TrueShares is model truth and is NEVER put on the
   * bridge — a publisher that writes it is a review-blocking defect.
   *
   * `pollsterName` is FLAVOR; `pollsterId` is engine-owned.
   */
  interface PollSummary {
    id: IdString;
    date: SimDateString;
    pollsterId: IdString;
    pollsterName: string;
    sampleSize: number;
    /** [0,1]. */
    marginOfError: number;
    /** [0,1]. */
    undecidedShare: number;
    /** [0,1]. */
    projectedTurnout: number;
    /** -1 when no election is scheduled. */
    weeksToElection: number;
    /** "" when no election is scheduled. */
    electionDate: SimDateString;
    /** Sorted by partyId ascending. Sums to 1 minus undecidedShare. */
    shares: PartyShare[];
  }

  // -- agora.districts -------------------------------------------------------------------------

  /** Population share by wealth tier. Sums to 1. */
  interface WealthSplit {
    low: number;
    middle: number;
    high: number;
  }

  /** Population share by education tier. Sums to 1. */
  interface EducationSplit {
    uneducated: number;
    poorlyEducated: number;
    educated: number;
    wellEducated: number;
    highlyEducated: number;
  }

  /** Population share by age band. Sums to 1. */
  interface AgeSplit {
    child: number;
    teen: number;
    adult: number;
    elderly: number;
  }

  /** All [0,1]. Per-district derived indices. */
  interface DistrictIndicesView {
    gentrification: number;
    commuteMisery: number;
    serviceCoverage: number;
    discontent: number;
    gini: number;
  }

  /**
   * `agora.districts.list` — sorted by `id` ordinal ascending. Empty value: [].
   * The row for a list view; open `agora.districts.detail` for the full split.
   */
  interface DistrictBrief {
    id: IdString;
    name: string;
    population: number;
    eligibleVoters: number;
    leadingPartyId: IdString;
    /** [0,1]. */
    leadingShare: number;
    /** "" in a one-party district. */
    runnerUpPartyId: IdString;
    /** [0,1]. leadingShare minus the runner-up's share. */
    margin: number;
    /** [0,1]. */
    turnout: number;
    /** [0,100]. */
    happiness: number;
    /** [0,1]. */
    discontent: number;
    /** True means some numbers here are city values wearing a district name — see DistrictDetail. */
    hasCityFallbacks: boolean;
  }

  /**
   * `agora.districts.detail` — a MAP binding keyed by district id, fetched only for the district
   * the panel is showing. An unknown key returns EMPTY_DISTRICT_DETAIL rather than throwing: a
   * district can be deleted by the player while its panel is open.
   *
   * `hasCityFallbacks` is a RENDERING OBLIGATION. When true, every field named in
   * `cityFallbackFields` is a city-wide number substituted for an unavailable district one. Those
   * fields must be visibly marked (dimmed + tooltip) and must never be presented as a local fact.
   */
  interface DistrictDetail {
    id: IdString;
    name: string;
    population: number;
    households: number;
    eligibleVoters: number;
    votesCast: number;
    /** [0,1]. */
    turnout: number;
    /** [0,100]. */
    happiness: number;
    /** [0,1]. */
    unemployment: number;
    winningPartyId: IdString;
    /** [0,1]. */
    margin: number;
    /** Seats returned by this district. 0 under a pure list system. */
    seats: number;
    decidedByTieBreak: boolean;
    /** Sorted by partyId ascending. */
    shares: PartyShare[];
    wealth: WealthSplit;
    education: EducationSplit;
    age: AgeSplit;
    indices: DistrictIndicesView;
    hasCityFallbacks: boolean;
    /** Property names of the fields above that fell back to a city value. Sorted ascending. */
    cityFallbackFields: string[];
  }

  /**
   * `agora.districts.crosstab` (map, keyed by district id) and `agora.districts.cityCrosstab`
   * (city aggregate). ALWAYS exactly 15 cells: wealth ascending (Low, Middle, High) then education
   * ascending (Uneducated .. HighlyEducated) — a fixed 3x5 grid with no holes, even where a cell
   * has zero population. Empty value: [].
   *
   * The engine models 60 blocs (3 wealth x 5 education x 4 age); this collapses the age axis so 15
   * rows cross the bridge instead of 60. `turnout` is vote-weighted across the four age bands, so
   * the disenfranchised child/teen bands drag it down correctly rather than being dropped.
   */
  interface CrosstabCell {
    wealth: WealthTierName;
    education: EducationTierName;
    population: number;
    /** [0,1]. Share of the district (or city) population. */
    populationShare: number;
    eligibleVoters: number;
    /** [0,1]. Vote-weighted across age bands. */
    turnout: number;
    /** "" when the cell has no voters. */
    leadingPartyId: IdString;
    /** [0,1]. */
    leadingShare: number;
    /** [0,100]. */
    happiness: number;
    /** [0,1]. */
    discontent: number;
  }

  /** `agora.districts.cityIndices` — all [0,1]. Empty value: EMPTY_CITY_INDICES. */
  interface CityIndices {
    gini: number;
    brainDrain: number;
    serviceInequality: number;
    commuteMisery: number;
    polarization: number;
    legitimacy: number;
    discontent: number;
  }

  // -- agora.news ------------------------------------------------------------------------------

  /**
   * `agora.news.feed` — sorted by `date` DESCENDING, then `id` ascending. Capped at 40 items
   * (AGORA_NEWS_FEED_MAX). Empty value: [].
   *
   * Prose bodies deliberately do NOT ride here. When `hasArticle` is true, fetch the body from
   * `agora.news.article` keyed by `id`, and only when the item is opened.
   *
   * `headline`, `summary` and `outletName` are FLAVOR.
   */
  interface NewsHeadline {
    id: IdString;
    date: SimDateString;
    kind: NewsKindName;
    headline: string;
    /** One line. The full body is in NewsArticle. */
    summary: string;
    outletId: IdString;
    outletName: string;
    /** 1-5 for event-derived items, 0 otherwise. */
    severity: number;
    /** "" when not about one party. */
    partyId: IdString;
    /** "" when city-wide. */
    districtId: IdString;
    /** "" when not tied to a timeline event. */
    eventId: IdString;
    hasArticle: boolean;
  }

  /**
   * `agora.news.article` — a MAP binding keyed by news item id. Fetched on open, never in bulk.
   * An unknown key returns EMPTY_NEWS_ARTICLE. Every field is FLAVOR; parse none of it.
   */
  interface NewsArticle {
    id: IdString;
    date: SimDateString;
    headline: string;
    byline: string;
    /** At most 120 words by the flavor contract. */
    body: string;
    /** Free-form tone tag from the flavor layer, e.g. "wry", "alarmed". "" when unset. */
    tone: string;
    outletId: IdString;
    outletName: string;
    /** Sorted ordinal ascending. */
    tags: string[];
    partyId: IdString;
    districtId: IdString;
    eventId: IdString;
  }

  /**
   * `agora.news.events` — active timeline events, sorted by `firedDate` DESCENDING then `id`
   * ascending. Capped at 25 (AGORA_EVENTS_MAX). Empty value: [].
   *
   * `title` is catalog-authored; `localAngle` is FLAVOR and may be "" when the LLM has not run.
   */
  interface TimelineEventBrief {
    id: IdString;
    /** The event's catalog date. */
    date: SimDateString;
    title: string;
    region: EventRegionName;
    origin: EventOriginName;
    /** 1-5. */
    severity: number;
    durationMonths: number;
    /** "" if not yet fired. */
    firedDate: SimDateString;
    /** "" if it does not expire. */
    expiresDate: SimDateString;
    archetypeId: IdString;
    /** FLAVOR. "" when the flavor layer has not produced one. */
    localAngle: string;
    /** Sorted ordinal ascending. */
    tags: string[];
    /** Districts this event's effects landed on. Empty means city-wide. Sorted ascending. */
    districtIds: IdString[];
  }

  /**
   * `agora.news.mandates` — the mandate tracker. Sorted by STATUS RANK ascending (Active 0,
   * Pending 1, PartiallyFulfilled 2, Fulfilled 3, Defied 4, Abandoned 5), then `deadlineDate`
   * ascending, then `id` ascending — so the tracker opens on what is live and closest to due.
   * Empty value: [].
   *
   * `isMeasurementStalled` means the metric is currently unreadable. Such a mandate is HELD, not
   * failing: render it as paused, never as behind schedule, and never let the clock defy it.
   *
   * `text` is FLAVOR. Every number here is engine-owned.
   */
  interface MandateRow {
    id: IdString;
    partyId: IdString;
    coalitionId: IdString;
    /** "" means city-wide. */
    districtId: IdString;
    issue: IssueName;
    metric: MandateMetricName;
    direction: MandateDirectionName;
    /** In the metric's own units, not normalised. */
    baselineValue: number;
    targetValue: number;
    currentValue: number;
    /** [0,1], clamped. */
    progress: number;
    issuedDate: SimDateString;
    deadlineDate: SimDateString;
    /** "" while unresolved. */
    resolvedDate: SimDateString;
    status: MandateStatusName;
    /** [0,1]. How much voters care. */
    salience: number;
    text: string;
    isMeasurementStalled: boolean;
    /** Months from the current sim date to the deadline. Negative once overdue. */
    monthsRemaining: number;
  }

  /**
   * `agora.news.flavorStatus` — LLM health, republished on every attempt, success or failure.
   * Empty value: EMPTY_FLAVOR_STATUS.
   *
   * A failed wake keeps the last good flavor by design (non-negotiable #7). The feed may be
   * unchanged after a wake; `lastError` may be the only visible consequence. Disable the wake
   * control while `pendingWake` is true.
   */
  interface FlavorStatus {
    /** Date of the last SUCCESSFUL flavor generation. "" if none ever succeeded. */
    lastFlavorDate: SimDateString;
    /** Date of the last attempt, successful or not. "" if never attempted. */
    lastAttemptDate: SimDateString;
    /** True when prose is older than the configured wake cadence expects. */
    isStale: boolean;
    providerAvailable: boolean;
    /** A wake is in flight. */
    pendingWake: boolean;
    lastError: FlavorErrorName;
    /** Number of articles currently held. */
    articleCount: number;
  }
}
